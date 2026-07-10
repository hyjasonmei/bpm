using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.OVERTIME.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.OVERTIME.V1;

/// <summary>
/// State machine for OVERTIME V1 (加班申請). One transition per public method;
/// persistence + notification calls run inside the caller's EF unit of work.
///
/// Status graph (mirrors the bundle's BPMN):
/// <code>
///   start ─► PendingManager ─approve─► [gateway 單月累計時數]
///                 │                        │ >20h ──► PendingHr ─approve─► Completed
///              reject                      │                     └reject─► RejectedByHr
///                 ▼                        └ ≤20h ──────────────────────► Completed
///          RejectedByManager
/// </code>
/// The submitter may withdraw (撤回) any non-terminal case → Cancelled.
///
/// Gateway note: <c>monthlyHours</c> is neither a form field nor a spec variable,
/// so it is computed server-side at manager approval as this case's 預估時數 plus the
/// submitter's other Completed overtime hours in the same calendar month. 預估時數 is
/// optional in the spec — a null value counts as 0.
/// </summary>
public sealed class OVERTIME_V1_OvertimeService(
    IOVERTIME_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<OVERTIME_V1_OvertimeService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "OVERTIME";
    public const int FlowVersion = 1;

    /// <summary>
    /// Second-stage 人資複核 role. The bundle's spec uses the shorthand
    /// <c>role:HR</c>; resolved against admin during planning to the seeded role
    /// code below (same HR queue LEAVE V1 archives to).
    /// </summary>
    public const string HrRoleCode = "HR_MANAGER";

    /// <summary>單月累計時數 threshold — over this routes to HR review (spec CEL: monthlyHours &gt; 20).</summary>
    public const decimal MonthlyHrThresholdHours = 20m;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        DateOnly OvertimeDate,
        string? StartTime,
        string? EndTime,
        decimal? EstimatedHours,
        string OvertimeReason);

    // ============================================================
    // task_apply.actions[submit]
    // ============================================================

    public async Task<OVERTIME_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var manager = await org.GetManagerIdAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval: submitter has no direct manager (submitter.manager resolved to nobody)");
        if (manager == input.SubmitterUserId)
            throw new ConflictException("cannot route approval: submitter is their own manager");

        var now = clock.UtcNow;
        var c = new OVERTIME_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            OvertimeDate = input.OvertimeDate,
            StartTime = Trimmed(input.StartTime),
            EndTime = Trimmed(input.EndTime),
            EstimatedHours = input.EstimatedHours,
            OvertimeReason = input.OvertimeReason.Trim(),
            Status = OVERTIME_V1_CaseStatus.PendingManager,
            ManagerUserId = manager,
            CurrentAssigneeUserId = manager,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifyAssignManagerAsync(c, manager.Value, ct);
        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal state
    /// (PendingManager / PendingHr) — once Completed / Rejected / Cancelled it can
    /// no longer be withdrawn. Clears the assignee so the case drops out of every
    /// inbox.
    /// </summary>
    public async Task<OVERTIME_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (IsTerminal(c.Status))
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        var now = clock.UtcNow;
        c.Status = OVERTIME_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = null;
        c.CompletedAt = now;
        c.LastActivityAt = now;
        await store.SaveChangesAsync(ct);
        log.LogInformation("OVERTIME/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    // ============================================================
    // approval_manager.actions[approve / reject]
    // ============================================================

    public Task<OVERTIME_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<OVERTIME_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<OVERTIME_V1_Case> ManagerDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != OVERTIME_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId is not { } manager || !await auth.CanActAsync(manager, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        var now = clock.UtcNow;
        c.ManagerApproved = approve;
        c.ManagerComment = Trimmed(comment);
        c.ManagerDecisionAt = now;
        c.LastActivityAt = now;

        if (!approve)
        {
            c.Status = OVERTIME_V1_CaseStatus.RejectedByManager;
            c.CurrentAssigneeUserId = null;
            c.CurrentAssigneeRoleCode = null;
            c.CompletedAt = now;
            await store.SaveChangesAsync(ct);
            log.LogInformation("OVERTIME/{CaseId}: rejected by manager", c.Id);
            await NotifyManagerRejectedAsync(c, ct);
            return c;
        }

        // Gateway gateway_hours: 單月累計時數 = this case + the submitter's other
        // Completed overtime hours in the same calendar month.
        var monthStart = new DateOnly(c.OvertimeDate.Year, c.OvertimeDate.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var priorHours = await store.SumCompletedHoursInMonthAsync(
            c.SubmitterUserId, monthStart, nextMonthStart, c.Id, ct);
        var monthlyHours = priorHours + (c.EstimatedHours ?? 0m);
        c.MonthlyHours = monthlyHours;

        if (monthlyHours > MonthlyHrThresholdHours)
        {
            var hrMembers = await directory.GetUsersInRoleAsync(HrRoleCode, ct);
            if (hrMembers.Count == 0)
                throw new ConflictException(
                    $"no active user holds role:{HrRoleCode}; cannot route 人資複核");

            c.Status = OVERTIME_V1_CaseStatus.PendingHr;
            c.CurrentAssigneeUserId = null;
            c.CurrentAssigneeRoleCode = HrRoleCode;   // pending on the HR role queue — any holder can act
            await store.SaveChangesAsync(ct);
            log.LogInformation(
                "OVERTIME/{CaseId}: manager approved, {Hours}h > {Threshold}h → PendingHr",
                c.Id, monthlyHours, MonthlyHrThresholdHours);
            await NotifyManagerApprovedAsync(c, ct);
            await NotifyHrPendingAsync(c, hrMembers, ct);
            return c;
        }

        // Under threshold — auto-complete after manager approval.
        c.Status = OVERTIME_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = null;
        c.CompletedAt = now;
        await store.SaveChangesAsync(ct);
        log.LogInformation(
            "OVERTIME/{CaseId}: manager approved, {Hours}h ≤ {Threshold}h → Completed",
            c.Id, monthlyHours, MonthlyHrThresholdHours);
        await NotifyFullyApprovedAsync(c, ct);
        return c;
    }

    // ============================================================
    // approval_hr.actions[approve / reject]
    // ============================================================

    public Task<OVERTIME_V1_Case> ApproveByHrAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => HrDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<OVERTIME_V1_Case> RejectByHrAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => HrDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<OVERTIME_V1_Case> HrDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != OVERTIME_V1_CaseStatus.PendingHr)
            throw new ConflictException($"case is in status {c.Status}, expected PendingHr");
        if (!await auth.CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId, ct))
            throw new ForbiddenException("only a holder of the HR role (or the assigned user / delegate) may act on this case");

        var now = clock.UtcNow;
        c.HrUserId = actorUserId;   // shared role queue: record who actually signed
        c.HrApproved = approve;
        c.HrComment = Trimmed(comment);
        c.HrDecisionAt = now;
        c.LastActivityAt = now;

        if (!approve)
        {
            c.Status = OVERTIME_V1_CaseStatus.RejectedByHr;
            c.CurrentAssigneeUserId = null;
            c.CurrentAssigneeRoleCode = null;
            c.CompletedAt = now;
            await store.SaveChangesAsync(ct);
            log.LogInformation("OVERTIME/{CaseId}: rejected by HR", c.Id);
            await NotifyHrRejectedAsync(c, ct);
            return c;
        }

        c.Status = OVERTIME_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = null;
        c.CompletedAt = now;
        await store.SaveChangesAsync(ct);
        log.LogInformation("OVERTIME/{CaseId}: approved by HR → Completed", c.Id);
        await NotifyFullyApprovedAsync(c, ct);
        return c;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private async Task<OVERTIME_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("OVERTIME_V1_Case", caseId);

    private static bool IsTerminal(OVERTIME_V1_CaseStatus s)
        => s is OVERTIME_V1_CaseStatus.Completed
             or OVERTIME_V1_CaseStatus.RejectedByManager
             or OVERTIME_V1_CaseStatus.RejectedByHr
             or OVERTIME_V1_CaseStatus.Cancelled;

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (input.OvertimeDate == default)
            throw Invalid(nameof(input.OvertimeDate), "加班日期 is required");
        if (string.IsNullOrWhiteSpace(input.OvertimeReason))
            throw Invalid(nameof(input.OvertimeReason), "加班事由 is required");
        if (input.EstimatedHours is < 0m)
            throw Invalid(nameof(input.EstimatedHours), "預估時數 cannot be negative");
    }

    // ── Notifications ──────────────────────────────────────────────────────

    private async Task NotifyAssignManagerAsync(OVERTIME_V1_Case c, Guid managerUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, managerUserId }, ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId);
        var manager = lookups.GetValueOrDefault(managerUserId);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderAssignManager(
            applicantName: applicant?.DisplayName ?? ShortIdLabel(c.SubmitterUserId),
            submitTime: c.SubmittedAt,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours,
            reason: c.OvertimeReason);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_on_submit_manager",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(managerUserId, manager?.Email, manager?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyManagerApprovedAsync(OVERTIME_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderManagerApproved(
            applicantName: submitter?.DisplayName ?? ShortIdLabel(c.SubmitterUserId),
            submitTime: c.SubmittedAt,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_on_approve_submitter",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyManagerRejectedAsync(OVERTIME_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderManagerRejected(
            applicantName: submitter?.DisplayName ?? ShortIdLabel(c.SubmitterUserId),
            submitTime: c.SubmittedAt,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours,
            rejectReason: c.ManagerComment);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_node_reject_manager",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyHrRejectedAsync(OVERTIME_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderHrRejected(
            applicantName: submitter?.DisplayName ?? ShortIdLabel(c.SubmitterUserId),
            submitTime: c.SubmittedAt,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours,
            rejectReason: c.HrComment);
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_node_reject_hr",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    /// Shared-role-queue: notify every current holder of the HR role (in_app) so
    /// any of them can pick the case up.
    private async Task NotifyHrPendingAsync(OVERTIME_V1_Case c, IReadOnlyList<Guid> hrMemberIds, CancellationToken ct)
    {
        if (hrMemberIds.Count == 0) return;
        var ids = hrMemberIds.Append(c.SubmitterUserId).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderAssignManager(
            applicantName: applicantName,
            submitTime: c.SubmittedAt,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours,
            reason: c.OvertimeReason);
        var recipients = hrMemberIds.Select(uid =>
        {
            var r = lookups.GetValueOrDefault(uid);
            return new NotifyRecipient(uid, r?.Email, r?.DisplayName);
        }).ToArray();
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_hr_pending",
            Subject:    $"【加班申請】{applicantName} 的加班申請待人資複核",
            Body:       rendered.Body,
            Channels:   new[] { "in_app" },
            Recipients: recipients,
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyFullyApprovedAsync(OVERTIME_V1_Case c, CancellationToken ct)
    {
        var hrMemberIds = await directory.GetUsersInRoleAsync(HrRoleCode, ct);
        var ids = hrMemberIds.Append(c.SubmitterUserId).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var rendered = OVERTIME_V1_NotificationTemplates.RenderFullyApproved(
            applicantName: applicantName,
            overtimeDate: c.OvertimeDate,
            estimatedHours: c.EstimatedHours,
            reason: c.OvertimeReason);

        var recipients = new List<NotifyRecipient>
        {
            new(c.SubmitterUserId,
                lookups.GetValueOrDefault(c.SubmitterUserId)?.Email,
                lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName),
        };
        recipients.AddRange(hrMemberIds.Select(uid =>
        {
            var r = lookups.GetValueOrDefault(uid);
            return new NotifyRecipient(uid, r?.Email, r?.DisplayName);
        }));

        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "OVERTIME_V1.notif_node_approve",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: recipients.ToArray(),
            Context:    NotificationContext(c)
        ), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(OVERTIME_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
        };

    private static string ShortIdLabel(Guid id) => id.ToString("N")[..8];
}

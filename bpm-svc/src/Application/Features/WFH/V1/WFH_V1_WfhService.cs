using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.WFH.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.WFH.V1;

/// <summary>
/// State machine for WFH (居家辦公申請) V1. One transition per public
/// method. Status graph (bundle nodes start_1 / task_apply /
/// approval_manager / gateway_days / approval_senior / end_*):
/// <code>
///   start ─► PendingManager ──approve, days &lt;= 7──► Completed
///                  │                  │
///                  │              approve, days &gt; 7
///                  │                  ▼
///                  │            PendingSenior ──approve──► Completed
///                  │                  │
///               reject             reject
///                  ▼                  ▼
///            ResubmitRequired ◄───────┘  (send-back; resubmit → PendingManager)
/// </code>
/// Manager approver = <c>submitter.manager</c>; senior approver =
/// <c>submitter.manager.manager</c> (falls back to the manager itself
/// when the manager is already top of the org chain).
/// </summary>
public sealed class WFH_V1_WfhService(
    IWFH_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<WFH_V1_WfhService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "WFH";
    public const int FlowVersion = 1;

    /// <summary>gateway_days threshold — consecutive days &gt; 7 routes to senior approval.</summary>
    public const int DaysGateThreshold = 7;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        DateOnly ApplyDate,
        DateOnly StartDate,
        DateOnly EndDate,
        string Reason,
        Guid? AttachmentFileId);

    public async Task<WFH_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval: submitter has no manager assigned, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new WFH_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            ApplyDate = input.ApplyDate,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Days = ComputeConsecutiveDays(input.StartDate, input.EndDate),
            Reason = input.Reason.Trim(),
            AttachmentFileId = input.AttachmentFileId,
            Status = WFH_V1_CaseStatus.PendingManager,
            ManagerUserId = manager,
            CurrentAssigneeUserId = manager,
            RoundCount = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifySubmittedAsync(c, ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    public async Task<WFH_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != WFH_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval on resubmit: submitter has no manager, or the manager is the submitter themselves");

        c.ApplyDate = input.ApplyDate;
        c.StartDate = input.StartDate;
        c.EndDate = input.EndDate;
        c.Days = ComputeConsecutiveDays(input.StartDate, input.EndDate);
        c.Reason = input.Reason.Trim();
        c.AttachmentFileId = input.AttachmentFileId;

        // Fresh approval round — clear both stages' decision columns.
        c.ManagerUserId = manager;
        c.ManagerApproved = null;
        c.ManagerComment = null;
        c.ManagerDecisionAt = null;
        c.SeniorUserId = null;
        c.SeniorApproved = null;
        c.SeniorComment = null;
        c.SeniorDecisionAt = null;

        c.CurrentAssigneeUserId = manager;
        c.Status = WFH_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (PendingManager / PendingSenior / ResubmitRequired); once
    /// Completed or already Cancelled it can no longer be withdrawn.
    /// Clears the assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<WFH_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is WFH_V1_CaseStatus.Completed or WFH_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = WFH_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("WFH/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    // ------------------------------------------------------------
    // Manager stage (approval_manager)
    // ------------------------------------------------------------

    public Task<WFH_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<WFH_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<WFH_V1_Case> ManagerDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != WFH_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve;
        c.ManagerComment = comment;
        c.ManagerDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
            return await SendBackAsync(c, comment, "主管退件", ct);

        // gateway_days: > 7 consecutive days → senior approval, else complete.
        if (c.Days > DaysGateThreshold)
        {
            var senior = await ResolveSeniorApproverAsync(c.SubmitterUserId, ct);
            if (senior is null)
                throw new ConflictException(
                    "cannot route senior approval: no manager.manager (or manager fallback) could be resolved");
            c.SeniorUserId = senior;
            c.CurrentAssigneeUserId = senior;
            c.Status = WFH_V1_CaseStatus.PendingSenior;
            await store.SaveChangesAsync(ct);
            log.LogInformation("WFH/{CaseId}: manager approved, days {Days} > {Threshold} → senior {Senior}",
                c.Id, c.Days, DaysGateThreshold, senior);
            await NotifyAssignAsync(c, senior.Value, ct);
            return c;
        }

        c.Status = WFH_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("WFH/{CaseId}: manager approved, days {Days} <= {Threshold} → completed",
            c.Id, c.Days, DaysGateThreshold);
        return c;
    }

    // ------------------------------------------------------------
    // Senior stage (approval_senior) — only on the days > 7 branch
    // ------------------------------------------------------------

    public Task<WFH_V1_Case> ApproveBySeniorAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SeniorDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<WFH_V1_Case> RejectBySeniorAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SeniorDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<WFH_V1_Case> SeniorDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != WFH_V1_CaseStatus.PendingSenior)
            throw new ConflictException($"case is in status {c.Status}, expected PendingSenior");
        if (c.SeniorUserId is not { } senior || !await auth.CanActAsync(senior, actorUserId, ct))
            throw new ForbiddenException("only the assigned senior approver (or their active delegate) may act on this case");

        c.SeniorApproved = approve;
        c.SeniorComment = comment;
        c.SeniorDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
            return await SendBackAsync(c, comment, "上級主管退件", ct);

        c.Status = WFH_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("WFH/{CaseId}: senior approved → completed", c.Id);
        return c;
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    /// <summary>
    /// Reject (either stage) routes the case back to the submitter for
    /// edit + resubmit (notify_slfp), not a hard terminal reject.
    /// </summary>
    private async Task<WFH_V1_Case> SendBackAsync(WFH_V1_Case c, string? comment, string defaultReason, CancellationToken ct)
    {
        c.Status = WFH_V1_CaseStatus.ResubmitRequired;
        c.CurrentAssigneeUserId = c.SubmitterUserId;
        await store.SaveChangesAsync(ct);
        log.LogInformation("WFH/{CaseId}: rejected → resubmit required (round {Round})", c.Id, c.RoundCount);
        await NotifyRejectAsync(c, string.IsNullOrWhiteSpace(comment) ? defaultReason : comment!, ct);
        return c;
    }

    private async Task<WFH_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("WFH_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (input.ApplyDate == default)
            throw Invalid(nameof(input.ApplyDate), "apply_date is required");
        if (input.StartDate == default)
            throw Invalid(nameof(input.StartDate), "wfh start date is required");
        if (input.EndDate == default)
            throw Invalid(nameof(input.EndDate), "wfh end date is required");
        if (input.EndDate < input.StartDate)
            throw Invalid(nameof(input.EndDate), "end date must be on or after start date");
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw Invalid(nameof(input.Reason), "reason is required");
    }

    /// <summary>
    /// Consecutive calendar days between start and end, inclusive — the
    /// natural reading of the gateway's "連續日期" (consecutive dates).
    /// Mirrors the TS-side <c>consecutiveDays</c> in the form so client +
    /// server agree on the value the gateway compares against.
    /// </summary>
    public static int ComputeConsecutiveDays(DateOnly start, DateOnly end)
        => end < start ? 0 : end.DayNumber - start.DayNumber + 1;

    /// <summary>Resolve submitter.manager; null on missing / self.</summary>
    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    /// <summary>
    /// Senior-stage approver. Spec wrote <c>submitter.manager</c> for the
    /// senior node, which Jason confirmed is a wizard slip — it should be
    /// the next level up (<c>submitter.manager.manager</c>). When the
    /// manager is already top of the org chain (no manager of their own),
    /// fall back to the manager themselves (per Jason's "就用最高層級").
    /// Returns null only when the submitter has no manager at all.
    /// </summary>
    public async Task<Guid?> ResolveSeniorApproverAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await ResolveManagerAsync(submitterUserId, ct);
        if (managerId is null) return null;

        var seniorId = await org.GetManagerIdAsync(managerId.Value, ct);
        // Manager is top of the chain → use the manager (top level) itself.
        if (seniorId is null || seniorId == managerId) return managerId;
        return seniorId;
    }

    // ------------------------------------------------------------
    // Notifications
    // ------------------------------------------------------------

    private async Task NotifySubmittedAsync(WFH_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = WFH_V1_NotificationTemplates.RenderSubmitted($"/cases/wfh/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "WFH_V1.notify_sgjz",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyAssignAsync(WFH_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var rendered = WFH_V1_NotificationTemplates.RenderAssign(
            applicantName, WFH_V1_NotificationTemplates.BuildSummary(c), $"/cases/wfh/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "WFH_V1.notify_shnv",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyRejectAsync(WFH_V1_Case c, string rejectReason, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = WFH_V1_NotificationTemplates.RenderReject(rejectReason, $"/cases/wfh/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "WFH_V1.notify_slfp",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(WFH_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
            ["round"]       = c.RoundCount.ToString(),
        };

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// State machine for VENDOR_EXPENSE V1 (採購申請流程). One transition per
/// public method. All persistence + notification calls happen inside the
/// caller's EF unit-of-work via <see cref="IVENDOR_EXPENSE_V1_CaseStore"/>.
///
/// Status graph (mirrors the bundle's BPMN):
/// <code>
///   start ─► PendingSupervisor ─approve─► PendingProcurement ─approve─► PendingSign ─approve─► Completed
///                  │                            │                            │
///               reject                       reject                       reject
///                  ▼                            ▼                            ▼
///                  └──────────────► ResubmitRequired ◄────────────────────────┘
///                                        │
///                                    resubmit (original submitter only)
///                                        │
///                                        ▼
///                                  PendingSupervisor (new round)
/// </code>
///
/// Chef-baked decisions (spec was silent / used a UUID):
///  - <c>approval_procurement</c>'s <c>role:c3a098d8-…</c> ActorRef is
///    resolved against admin by NAME via <see cref="ProcurementRoleName"/>;
///    the bundle gives only the role's UUID, and admin's seeded role
///    graph carries a matching "Procurement" role (採購審定).
///  - <c>approval_sign</c> has a reject button but no reject edge in the
///    BPMN; its reject bounces back to the submitter for resubmit, the
///    same shape as the two earlier gateways and the on_reject template.
///  - stages 1 (主管審核) and 3 (簽核) both resolve to
///    <c>submitter.department.head</c> per the spec — the same person
///    reviews then signs.
/// </summary>
public sealed class VENDOR_EXPENSE_V1_VendorExpenseService(
    IVENDOR_EXPENSE_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<VENDOR_EXPENSE_V1_VendorExpenseService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "VENDOR_EXPENSE";
    public const int FlowVersion = 1;

    /// <summary>
    /// Role NAME for the 採購審定 (procurement) stage. The bundle's spec
    /// uses <c>role:c3a098d8-d377-4f45-a1bb-0cc23386a7c1</c> (a UUID, not
    /// a name); resolved against admin's seeded role graph to the literal
    /// role name below. ⚠️ admin seeds the role but assigns no members by
    /// default — a Procurement member must exist for the live flow to
    /// route past supervisor approval.
    /// </summary>
    public const string ProcurementRoleName = "PROCUREMENT";

    private const string NoReasonGiven = "（未填寫原因）";

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string? Vendor,
        string? SubmitterComment,
        IReadOnlyList<VENDOR_EXPENSE_V1_Invoice> Invoices);

    // ============================================================
    // task_fill.actions[submit]  /  task_fill (resubmit on rejection)
    // ============================================================

    public async Task<VENDOR_EXPENSE_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var supervisor = await ResolveDeptHeadAsync(input.SubmitterUserId, ct);
        if (supervisor is null)
            throw new ConflictException(
                "cannot route approval: submitter has no primary department, or the department has no head, or the head is the submitter themselves");

        var now = clock.UtcNow;
        var c = new VENDOR_EXPENSE_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            Vendor = input.Vendor?.Trim(),
            SubmitterComment = input.SubmitterComment?.Trim(),
            Invoices = input.Invoices.ToList(),
            Status = VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor,
            SupervisorUserId = supervisor,
            CurrentAssigneeUserId = supervisor,
            RoundCount = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifySubmittedAsync(c, ct);
        return c;
    }

    public async Task<VENDOR_EXPENSE_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);

        var supervisor = await ResolveDeptHeadAsync(input.SubmitterUserId, ct);
        if (supervisor is null)
            throw new ConflictException(
                "cannot route approval on resubmit: submitter has no primary department, or the head is the submitter themselves");

        // New round: clear every stage's decision columns so the timeline
        // reflects the latest pass only.
        c.Vendor = input.Vendor?.Trim();
        c.SubmitterComment = input.SubmitterComment?.Trim();
        c.Invoices = input.Invoices.ToList();
        c.SupervisorUserId = supervisor;
        c.SupervisorApproved = null;
        c.SupervisorComment = null;
        c.SupervisorDecisionAt = null;
        c.ProcurementUserId = null;
        c.ProcurementApproved = null;
        c.ProcurementComment = null;
        c.ProcurementDecisionAt = null;
        c.SignUserId = null;
        c.SignApproved = null;
        c.SignComment = null;
        c.SignDecisionAt = null;
        c.CurrentAssigneeUserId = supervisor;
        c.Status = VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        return c;
    }

    // ============================================================
    // Submitter withdraw (撤回)
    // ============================================================

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (all three pending stages or ResubmitRequired) — once
    /// Completed or already Cancelled it can no longer be withdrawn.
    /// Clears the assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<VENDOR_EXPENSE_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is VENDOR_EXPENSE_V1_CaseStatus.Completed or VENDOR_EXPENSE_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = VENDOR_EXPENSE_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("VENDOR_EXPENSE/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    // ============================================================
    // approval_supervisor.actions[approve / reject]  (主管審核 → gateway_supervisor)
    // ============================================================

    public Task<VENDOR_EXPENSE_V1_Case> ApproveBySupervisorAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SupervisorDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<VENDOR_EXPENSE_V1_Case> RejectBySupervisorAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SupervisorDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<VENDOR_EXPENSE_V1_Case> SupervisorDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor)
            throw new ConflictException($"case is in status {c.Status}, expected PendingSupervisor");
        if (c.CurrentAssigneeUserId is not { } supervisor || !await auth.CanActAsync(supervisor, actorUserId, ct))
            throw new ForbiddenException("only the assigned supervisor (or their active delegate) may act on this case");

        c.SupervisorApproved = approve;
        c.SupervisorComment = comment;
        c.SupervisorDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
            return await BounceToResubmitAsync(c, "supervisor", comment, ct);

        var procurementMembers = await directory.GetUsersInRoleAsync(ProcurementRoleName, ct);
        if (procurementMembers.Count == 0)
            throw new ConflictException(
                $"no active user holds role:{ProcurementRoleName}; cannot route procurement approval");
        c.ProcurementUserId = null;                          // shared role queue — no single designated procurement user
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = ProcurementRoleName;     // pending on role:PROCUREMENT — any holder can act
        c.Status = VENDOR_EXPENSE_V1_CaseStatus.PendingProcurement;
        await store.SaveChangesAsync(ct);
        await NotifyRoleAsync(c, procurementMembers, ct);
        return c;
    }

    // ============================================================
    // approval_procurement.actions[approve / reject]  (採購審定 → gateway_procurement)
    // ============================================================

    public Task<VENDOR_EXPENSE_V1_Case> ApproveByProcurementAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ProcurementDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<VENDOR_EXPENSE_V1_Case> RejectByProcurementAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ProcurementDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<VENDOR_EXPENSE_V1_Case> ProcurementDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != VENDOR_EXPENSE_V1_CaseStatus.PendingProcurement)
            throw new ConflictException($"case is in status {c.Status}, expected PendingProcurement");
        if (!await auth.CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId, ct))
            throw new ForbiddenException("only a holder of the procurement role (or the assigned user / delegate) may act on this case");

        c.ProcurementUserId = actorUserId;   // shared role queue: record who actually signed
        c.ProcurementApproved = approve;
        c.ProcurementComment = comment;
        c.ProcurementDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
            return await BounceToResubmitAsync(c, "procurement", comment, ct);

        // 簽核 stage resolves to the submitter's department head again
        // (same ActorRef as the supervisor stage) — a specific user, so we
        // leave the shared role queue and re-assign to that user.
        var signer = await ResolveDeptHeadAsync(c.SubmitterUserId, ct);
        if (signer is null)
            throw new ConflictException(
                "cannot route sign-off: submitter has no department head (or it is the submitter)");

        c.SignUserId = signer;
        c.CurrentAssigneeUserId = signer;
        c.CurrentAssigneeRoleCode = null;    // leaving the role step → user-assigned sign
        c.Status = VENDOR_EXPENSE_V1_CaseStatus.PendingSign;
        await store.SaveChangesAsync(ct);
        return c;
    }

    // ============================================================
    // approval_sign.actions[approve / reject]  (簽核 → end_1)
    // ============================================================

    public Task<VENDOR_EXPENSE_V1_Case> ApproveBySignAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SignDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<VENDOR_EXPENSE_V1_Case> RejectBySignAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => SignDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<VENDOR_EXPENSE_V1_Case> SignDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != VENDOR_EXPENSE_V1_CaseStatus.PendingSign)
            throw new ConflictException($"case is in status {c.Status}, expected PendingSign");
        if (c.CurrentAssigneeUserId is not { } signer || !await auth.CanActAsync(signer, actorUserId, ct))
            throw new ForbiddenException("only the assigned signer (or their active delegate) may act on this case");

        c.SignApproved = approve;
        c.SignComment = comment;
        c.SignDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        // No reject edge in the BPMN for approval_sign, but it carries a
        // reject button + the on_reject template says "請修正後重新送件" —
        // so a sign-stage reject bounces back to the submitter too.
        if (!approve)
            return await BounceToResubmitAsync(c, "sign", comment, ct);

        c.Status = VENDOR_EXPENSE_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    // ============================================================
    // Helpers
    // ============================================================

    /// <summary>
    /// Common reject path: revert the assignee to the submitter, flip to
    /// ResubmitRequired, persist, and fire the on_reject notification.
    /// </summary>
    private async Task<VENDOR_EXPENSE_V1_Case> BounceToResubmitAsync(
        VENDOR_EXPENSE_V1_Case c, string stage, string? comment, CancellationToken ct)
    {
        c.Status = VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired;
        c.CurrentAssigneeUserId = c.SubmitterUserId;
        c.CurrentAssigneeRoleCode = null;            // leaving any role step
        await store.SaveChangesAsync(ct);
        log.LogInformation("VENDOR_EXPENSE/{CaseId}: {Stage} rejected (round {Round})", c.Id, stage, c.RoundCount);
        await NotifyRejectedAsync(c, comment, ct);
        return c;
    }

    private async Task<VENDOR_EXPENSE_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("VENDOR_EXPENSE_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    /// <summary>
    /// Spec validation: the repeater sets <c>minCount: 1</c> so at least
    /// one invoice row is required; no per-field <c>required</c> flags
    /// and the submit action's <c>promptComment: "optional"</c> mean the
    /// comment + invoice fields are not individually mandatory.
    /// </summary>
    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (input.Invoices.Count < 1)
            throw Invalid(nameof(input.Invoices), "at least one invoice row is required");
    }

    /// <summary>
    /// Resolve the submitter's primary-department head
    /// (<c>submitter.department.head</c>). No fallback is declared, so a
    /// missing dept / missing head / self-as-head all surface as a
    /// <see cref="ConflictException"/> on the caller (this returns null
    /// for any of those cases).
    /// </summary>
    public async Task<Guid?> ResolveDeptHeadAsync(Guid submitterUserId, CancellationToken ct)
    {
        var deptId = await org.GetPrimaryDepartmentIdAsync(submitterUserId, ct);
        if (deptId is null) return null;

        var headId = await org.GetDepartmentHeadIdAsync(deptId.Value, ct);
        if (headId is null) return null;

        // Self-approval guard — the head can't approve their own request.
        if (headId == submitterUserId) return null;
        return headId;
    }

    private async Task NotifySubmittedAsync(VENDOR_EXPENSE_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = VENDOR_EXPENSE_V1_NotificationTemplates.RenderSubmitted(
            caseUrl: CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "VENDOR_EXPENSE_V1.notify_qkje",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyRejectedAsync(VENDOR_EXPENSE_V1_Case c, string? comment, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var reason = string.IsNullOrWhiteSpace(comment) ? NoReasonGiven : comment!.Trim();
        var rendered = VENDOR_EXPENSE_V1_NotificationTemplates.RenderRejected(
            rejectReason: reason,
            caseUrl: CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "VENDOR_EXPENSE_V1.notify_qs19",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    /// Shared-role-queue: notify every current holder of the role the case is
    /// now pending on (in_app), so any of them can pick it up.
    private async Task NotifyRoleAsync(VENDOR_EXPENSE_V1_Case c, IReadOnlyList<Guid> memberUserIds, CancellationToken ct)
    {
        if (memberUserIds.Count == 0) return;
        var ids = memberUserIds.Append(c.SubmitterUserId).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? c.SubmitterUserId.ToString("N")[..8];
        var rendered = VENDOR_EXPENSE_V1_NotificationTemplates.RenderRoleAssign(
            applicantName: applicantName,
            summary: BuildSummary(c),
            caseUrl: CaseUrl(c));
        var recipients = memberUserIds.Select(uid =>
        {
            var r = lookups.GetValueOrDefault(uid);
            return new NotifyRecipient(uid, r?.Email, r?.DisplayName);
        }).ToArray();
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "VENDOR_EXPENSE_V1.notify_role_assign",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "in_app" },
            Recipients: recipients,
            Context:    NotificationContext(c)
        ), ct);
    }

    private static string CaseUrl(VENDOR_EXPENSE_V1_Case c) => $"/cases/vendor_expense/{c.Id}";

    private static IReadOnlyDictionary<string, string?> NotificationContext(VENDOR_EXPENSE_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
            ["round"]       = c.RoundCount.ToString(),
        };

    public static string BuildSummary(VENDOR_EXPENSE_V1_Case c)
    {
        var n = c.Invoices.Count;
        var noun = n == 1 ? "invoice" : "invoices";
        var vendor = string.IsNullOrWhiteSpace(c.Vendor) ? "採購申請" : c.Vendor!.Trim();
        return $"{vendor} — {n} {noun}";
    }
}

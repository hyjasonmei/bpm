using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// State machine for PURCHASE_REQUEST V1. One transition per public
/// method. All persistence + notification calls happen inside the
/// caller's EF unit-of-work — controllers call
/// <see cref="IPURCHASE_REQUEST_V1_CaseStore.SaveChangesAsync"/> via
/// the methods below.
///
/// Status graph (mirrors the bundle's BPMN):
/// <code>
///   ┌─ start ──► PendingDeptHead ──approve──► PendingFinance ──approve──► Completed
///   │                │                              │
///   │             reject                         reject
///   │                ▼                              ▼
///   └──────── ResubmitRequired ◄────────────────────┘
///                    │
///                resubmit
///                    │
///                    ▼
///              PendingDeptHead (again)
/// </code>
/// </summary>
public sealed class PURCHASE_REQUEST_V1_PurchaseRequestService(
    IPURCHASE_REQUEST_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<PURCHASE_REQUEST_V1_PurchaseRequestService> log,
    INotifyDispatcher notify)
{
    public const string FlowCode = "PURCHASE_REQUEST";
    public const int FlowVersion = 1;

    /// <summary>
    /// Role NAME for the second-stage approver. The bundle's spec uses
    /// <c>role:c3a098d8-d377-4f45-a1bb-0cc23386a7c1</c>; resolved
    /// against admin during planning to the literal role name below.
    /// </summary>
    public const string FinanceRoleName = "Finance";

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string SubmitterComment,
        IReadOnlyList<PURCHASE_REQUEST_V1_Invoice> Invoices);

    // ============================================================
    // task_apply.actions[submit]  /  task_apply (resubmit on rejection)
    // ============================================================

    public async Task<PURCHASE_REQUEST_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var deptHead = await ResolveDeptHeadAsync(input.SubmitterUserId, ct);
        if (deptHead is null)
            throw new ConflictException(
                "cannot route approval: submitter has no primary department, or the department has no head, or the head is the submitter themselves");

        var now = clock.UtcNow;
        var c = new PURCHASE_REQUEST_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            SubmitterComment = input.SubmitterComment.Trim(),
            Invoices = input.Invoices.ToList(),
            Status = PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead,
            DeptHeadUserId = deptHead,
            CurrentAssigneeUserId = deptHead,
            RoundCount = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifySubmittedAsync(c, ct);
        await NotifyAssignAsync(c, deptHead.Value, ct);
        return c;
    }

    public async Task<PURCHASE_REQUEST_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);

        var deptHead = await ResolveDeptHeadAsync(input.SubmitterUserId, ct);
        if (deptHead is null)
            throw new ConflictException(
                "cannot route approval on resubmit: submitter has no primary department, or the head is the submitter themselves");

        // New round: clear the previous decision columns so the timeline
        // reflects the latest pass only.
        c.SubmitterComment = input.SubmitterComment.Trim();
        c.Invoices = input.Invoices.ToList();
        c.DeptHeadUserId = deptHead;
        c.DeptHeadApproved = null;
        c.DeptHeadComment = null;
        c.DeptHeadDecisionAt = null;
        c.FinanceUserId = null;
        c.FinanceApproved = null;
        c.FinanceComment = null;
        c.FinanceDecisionAt = null;
        c.CurrentAssigneeUserId = deptHead;
        c.Status = PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await NotifyAssignAsync(c, deptHead.Value, ct);
        return c;
    }

    // ============================================================
    // approval_dept_head.actions[approve / reject]
    // ============================================================

    public Task<PURCHASE_REQUEST_V1_Case> ApproveByDeptHeadAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => DeptHeadDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<PURCHASE_REQUEST_V1_Case> RejectByDeptHeadAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => DeptHeadDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<PURCHASE_REQUEST_V1_Case> DeptHeadDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead)
            throw new ConflictException($"case is in status {c.Status}, expected PendingDeptHead");
        if (c.DeptHeadUserId != actorUserId)
            throw new ForbiddenException("only the assigned department head may act on this case");

        c.DeptHeadApproved = approve;
        c.DeptHeadComment = comment;
        c.DeptHeadDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("PURCHASE_REQUEST/{CaseId}: dept-head rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        var financeUser = await directory.FindFirstUserInRoleAsync(FinanceRoleName, ct);
        if (financeUser is null)
            throw new ConflictException(
                $"no active user assigned to role:{FinanceRoleName}; cannot route finance approval");

        c.FinanceUserId = financeUser;
        c.CurrentAssigneeUserId = financeUser;
        c.Status = PURCHASE_REQUEST_V1_CaseStatus.PendingFinance;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, financeUser.Value, ct);
        return c;
    }

    // ============================================================
    // approval_finance.actions[approve / reject]
    // ============================================================

    public Task<PURCHASE_REQUEST_V1_Case> ApproveByFinanceAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => FinanceDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<PURCHASE_REQUEST_V1_Case> RejectByFinanceAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => FinanceDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<PURCHASE_REQUEST_V1_Case> FinanceDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != PURCHASE_REQUEST_V1_CaseStatus.PendingFinance)
            throw new ConflictException($"case is in status {c.Status}, expected PendingFinance");
        if (c.FinanceUserId != actorUserId)
            throw new ForbiddenException("only the assigned finance approver may act on this case");

        c.FinanceApproved = approve;
        c.FinanceComment = comment;
        c.FinanceDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("PURCHASE_REQUEST/{CaseId}: finance rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = PURCHASE_REQUEST_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private async Task<PURCHASE_REQUEST_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("PURCHASE_REQUEST_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.SubmitterComment))
            throw Invalid(nameof(input.SubmitterComment), "submitter comment is required");
        if (input.Invoices.Count < 1)
            throw Invalid(nameof(input.Invoices), "at least one invoice is required");
        for (var i = 0; i < input.Invoices.Count; i++)
        {
            var inv = input.Invoices[i];
            if (inv.InvoiceDate == default)
                throw Invalid($"invoices[{i}].invoiceDate", "invoice_date is required");
        }
    }

    /// <summary>
    /// Resolve the submitter's primary-department head. Spec uses
    /// <c>submitter.department.head</c>; no fallback is declared, so a
    /// missing dept / missing head / self-as-head all surface as a
    /// <see cref="ConflictException"/> on the caller. Returning null
    /// here means "couldn't resolve" — the caller maps that to the
    /// exception.
    /// </summary>
    public async Task<Guid?> ResolveDeptHeadAsync(Guid submitterUserId, CancellationToken ct)
    {
        var deptId = await org.GetPrimaryDepartmentIdAsync(submitterUserId, ct);
        if (deptId is null) return null;

        var headId = await org.GetDepartmentHeadIdAsync(deptId.Value, ct);
        if (headId is null) return null;

        // Self-approval guard — head can't approve their own purchase request.
        if (headId == submitterUserId) return null;
        return headId;
    }

    private async Task NotifySubmittedAsync(PURCHASE_REQUEST_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = PURCHASE_REQUEST_V1_NotificationTemplates.RenderSubmitted(
            caseUrl: $"/cases/purchase-request/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "PURCHASE_REQUEST_V1.notify_ckqd",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyAssignAsync(PURCHASE_REQUEST_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName
                            ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var summary = BuildSummary(c);
        var rendered = PURCHASE_REQUEST_V1_NotificationTemplates.RenderAssign(
            applicantName: applicantName,
            summary: summary,
            caseUrl: $"/cases/purchase-request/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "PURCHASE_REQUEST_V1.notify_crme",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(PURCHASE_REQUEST_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
            ["round"]       = c.RoundCount.ToString(),
        };

    public static string BuildSummary(PURCHASE_REQUEST_V1_Case c)
    {
        var n = c.Invoices.Count;
        var noun = n == 1 ? "invoice" : "invoices";
        return $"採購申請 — {n} {noun}";
    }

    private static string ShortIdLabel(Guid id)
        => id.ToString("N").Substring(0, 8);
}

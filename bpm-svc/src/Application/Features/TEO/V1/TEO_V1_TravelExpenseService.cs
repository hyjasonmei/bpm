using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.TEO.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.TEO.V1;

/// <summary>
/// State machine for TEO (Travel Expense) V1. Two-stage approval:
/// <code>
///   start ─► PendingManager ──approve──► PendingFinance ──approve──► Completed
///                │                            │
///             reject                       reject
///                ▼                            ▼
///          ResubmitRequired ◄─────────────────┘
///                │  resubmit
///                ▼
///          PendingManager (again)
/// </code>
/// Stage 1 approver = <c>submitter.manager</c> (node <c>ap</c>); stage 2
/// = <c>role:Finance</c> (node <c>fin</c>; the bundle's <c>role:VP</c>
/// was remapped during planning — VP has no members in the org).
/// </summary>
public sealed class TEO_V1_TravelExpenseService(
    ITEO_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<TEO_V1_TravelExpenseService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "TEO";
    public const int FlowVersion = 1;
    public const string FinanceRoleName = "FINANCE";

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string TravelRequestNo,
        IReadOnlyList<TEO_V1_ExpenseItem> ExpenseItems);

    public async Task<TEO_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval: submitter has no manager, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new TEO_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            TravelRequestNo = input.TravelRequestNo.Trim(),
            ExpenseItems = input.ExpenseItems.ToList(),
            Status = TEO_V1_CaseStatus.PendingManager,
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

    public async Task<TEO_V1_Case> ResubmitAsync(Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != TEO_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval on resubmit: submitter has no manager");

        c.TravelRequestNo = input.TravelRequestNo.Trim();
        c.ExpenseItems = input.ExpenseItems.ToList();
        c.ManagerUserId = manager;
        c.ManagerApproved = null; c.ManagerComment = null; c.ManagerDecisionAt = null;
        c.FinanceUserId = null; c.FinanceApproved = null; c.FinanceComment = null; c.FinanceDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = TEO_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (PendingManager, PendingFinance, or ResubmitRequired) — once
    /// Completed or already Cancelled it can no longer be withdrawn. Clears
    /// the assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<TEO_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is TEO_V1_CaseStatus.Completed or TEO_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = TEO_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("TEO/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    public Task<TEO_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<TEO_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<TEO_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != TEO_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.CurrentAssigneeUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve; c.ManagerComment = comment; c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = TEO_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("TEO/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        var financeMembers = await directory.GetUsersInRoleAsync(FinanceRoleName, ct);
        if (financeMembers.Count == 0)
            throw new ConflictException($"no active user holds role:{FinanceRoleName}; cannot route finance approval");
        c.FinanceUserId = null;                        // shared role queue — no single designated finance user
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = FinanceRoleName;   // pending on role:FINANCE — any holder can act
        c.Status = TEO_V1_CaseStatus.PendingFinance;
        await store.SaveChangesAsync(ct);
        await NotifyRoleAsync(c, financeMembers, ct);
        return c;
    }

    public Task<TEO_V1_Case> ApproveByFinanceAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => FinanceDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<TEO_V1_Case> RejectByFinanceAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => FinanceDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<TEO_V1_Case> FinanceDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != TEO_V1_CaseStatus.PendingFinance)
            throw new ConflictException($"case is in status {c.Status}, expected PendingFinance");
        if (!await auth.CanActAsync(c.CurrentAssigneeUserId, c.CurrentAssigneeRoleCode, actorUserId, ct))
            throw new ForbiddenException("only a holder of the finance role (or the assigned user / delegate) may act on this case");

        c.FinanceUserId = actorUserId;   // shared role queue: record who actually signed
        c.FinanceApproved = approve; c.FinanceComment = comment; c.FinanceDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = TEO_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            c.CurrentAssigneeRoleCode = null;            // leaving the role step
            await store.SaveChangesAsync(ct);
            log.LogInformation("TEO/{CaseId}: finance rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = TEO_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CurrentAssigneeRoleCode = null;                // leaving the role step
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    private async Task<TEO_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct) ?? throw new NotFoundException("TEO_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TravelRequestNo))
            throw Invalid(nameof(input.TravelRequestNo), "linked travel-request no is required");
        if (input.ExpenseItems.Count < 1)
            throw Invalid(nameof(input.ExpenseItems), "at least one expense item is required");
        for (var i = 0; i < input.ExpenseItems.Count; i++)
            if (input.ExpenseItems[i].Date == default)
                throw Invalid($"expenseItems[{i}].date", "expense item date is required");
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(TEO_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = TEO_V1_NotificationTemplates.RenderSubmitted($"/cases/teo/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "TEO_V1.notify_submitted", Subject: rendered.Subject, Body: rendered.Body,
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: NotificationContext(c)), ct);
    }

    private async Task NotifyAssignAsync(TEO_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var rendered = TEO_V1_NotificationTemplates.RenderAssign(applicantName, BuildSummary(c), $"/cases/teo/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "TEO_V1.notify_n_submit", Subject: rendered.Subject, Body: rendered.Body,
            Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            Context: NotificationContext(c)), ct);
    }

    /// Shared-role-queue: notify every current holder of the role the case is
    /// now pending on (in_app), so any of them can pick it up.
    private async Task NotifyRoleAsync(TEO_V1_Case c, IReadOnlyList<Guid> memberUserIds, CancellationToken ct)
    {
        if (memberUserIds.Count == 0) return;
        var ids = memberUserIds.Append(c.SubmitterUserId).Distinct().ToArray();
        var lookups = await directory.GetManyAsync(ids, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var rendered = TEO_V1_NotificationTemplates.RenderAssign(applicantName, BuildSummary(c), $"/cases/teo/{c.Id}");
        var recipients = memberUserIds.Select(uid =>
        {
            var r = lookups.GetValueOrDefault(uid);
            return new NotifyRecipient(uid, r?.Email, r?.DisplayName);
        }).ToArray();
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "TEO_V1.notify_n_submit", Subject: rendered.Subject, Body: rendered.Body,
            Channels: new[] { "in_app" },
            Recipients: recipients,
            Context: NotificationContext(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(TEO_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode, ["flowVersion"] = FlowVersion.ToString(),
            ["stage"] = c.Status.ToString(), ["round"] = c.RoundCount.ToString(),
        };

    public static string BuildSummary(TEO_V1_Case c)
        => $"差旅費用核銷 — {c.ExpenseItems.Count} 筆（{c.TravelRequestNo}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

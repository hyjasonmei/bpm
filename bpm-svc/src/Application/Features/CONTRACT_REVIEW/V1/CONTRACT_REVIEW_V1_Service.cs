using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Parallel;
using FluentValidation.Results;

namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// State machine for CONTRACT_REVIEW V1 (合約審查). Submit opens a parallel
/// review gateway (LEGAL + FINANCE 並簽, threshold 2/2 = AND). Each approver's
/// decision goes through the shared parallel primitive; the case advances when
/// the group resolves (all approve → Completed, any reject → Rejected).
/// </summary>
public sealed class CONTRACT_REVIEW_V1_Service(
    ICONTRACT_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IClock clock,
    INotifyDispatcher notify,
    IPrincipalDirectory directory)
{
    public const string FlowCode = "CONTRACT_REVIEW";
    public const int FlowVersion = 1;

    // BPMN node ids — MUST match the shipped .bpmn.xml.
    public const string ReviewGatewayNodeId = "gw_review";
    public const string LegalNodeId = "task_legal";
    public const string FinanceNodeId = "task_finance";
    public const string LegalRole = "LEGAL";
    public const string FinanceRole = "FINANCE";

    public sealed record SubmitInput(
        Guid SubmitterUserId, string Title, string Counterparty, decimal Amount, string Currency, Guid? ContractFileId);

    public async Task<CONTRACT_REVIEW_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            throw Invalid(nameof(input.Title), "title is required");
        if (string.IsNullOrWhiteSpace(input.Counterparty))
            throw Invalid(nameof(input.Counterparty), "counterparty is required");
        if (input.Amount < 0)
            throw Invalid(nameof(input.Amount), "amount must be >= 0");

        var now = clock.UtcNow;
        var c = new CONTRACT_REVIEW_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            Title = input.Title.Trim(),
            Counterparty = input.Counterparty.Trim(),
            Amount = input.Amount,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "NTD" : input.Currency.Trim(),
            ContractFileId = input.ContractFileId,
            Status = CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        // Open the parallel review: LEGAL + FINANCE, both required (threshold 2/2).
        await parallel.OpenAsync(FlowCode, FlowVersion, c.Id, ReviewGatewayNodeId,
            new List<SlotSpec>
            {
                new(LegalNodeId, LegalRole, null),
                new(FinanceNodeId, FinanceRole, null),
            },
            threshold: 2, ct);

        await NotifySubmittedAsync(c, ct);
        await NotifyApproversAsync(c, new[] { LegalRole, FinanceRole }, ct);
        return c;
    }

    public async Task<CONTRACT_REVIEW_V1_Case> DecideAsync(Guid caseId, Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(CONTRACT_REVIEW_V1_Case), caseId);
        if (c.Status != CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview)
            throw new ConflictException("case is not pending review");

        var result = await parallel.DecideAsync(slotId, actorUserId, approve, comment, ct);

        c.LastActivityAt = clock.UtcNow;
        var resolved = ParallelGroupStatus.Open;
        if (result.GroupStatus == ParallelGroupStatus.Approved)
        {
            c.Status = CONTRACT_REVIEW_V1_CaseStatus.Completed;
            c.CompletedAt = clock.UtcNow;
            resolved = ParallelGroupStatus.Approved;
        }
        else if (result.GroupStatus == ParallelGroupStatus.Rejected)
        {
            c.Status = CONTRACT_REVIEW_V1_CaseStatus.Rejected;
            resolved = ParallelGroupStatus.Rejected;
        }
        // else Open — case stays PendingParallelReview.

        await store.SaveChangesAsync(ct);

        if (resolved == ParallelGroupStatus.Approved) await NotifyResultAsync(c, approved: true, ct);
        else if (resolved == ParallelGroupStatus.Rejected) await NotifyResultAsync(c, approved: false, ct);
        return c;
    }

    // ── notifications ───────────────────────────────────────────────────────
    private string CaseUrl(CONTRACT_REVIEW_V1_Case c) => $"/cases/contract-review/{c.Id}";

    private async Task NotifySubmittedAsync(CONTRACT_REVIEW_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = CONTRACT_REVIEW_V1_NotificationTemplates.RenderSubmitted(c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_submitted",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    /// <summary>Notify every holder of each parallel-branch role that a case awaits their 並簽.</summary>
    private async Task NotifyApproversAsync(CONTRACT_REVIEW_V1_Case c, IReadOnlyList<string> roleCodes, CancellationToken ct)
    {
        var userIds = new List<Guid>();
        foreach (var role in roleCodes)
            userIds.AddRange(await directory.GetUsersInRoleAsync(role, ct));
        userIds = userIds.Distinct().ToList();
        if (userIds.Count == 0) return;

        var lookups = await directory.GetManyAsync(userIds.Append(c.SubmitterUserId).ToArray(), ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? c.SubmitterUserId.ToString()[..8];
        var r = CONTRACT_REVIEW_V1_NotificationTemplates.RenderParallelAssign(applicant, c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_parallel_assign",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: userIds.Select(id => new NotifyRecipient(id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyResultAsync(CONTRACT_REVIEW_V1_Case c, bool approved, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = approved
            ? CONTRACT_REVIEW_V1_NotificationTemplates.RenderCompleted(c.Title, CaseUrl(c))
            : CONTRACT_REVIEW_V1_NotificationTemplates.RenderRejected(c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_{(approved ? "completed" : "rejected")}",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> Ctx(CONTRACT_REVIEW_V1_Case c)
        => new Dictionary<string, string?> { ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode };

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}

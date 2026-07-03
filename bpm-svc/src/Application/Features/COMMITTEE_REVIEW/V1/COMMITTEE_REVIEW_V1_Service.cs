using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Parallel;
using FluentValidation.Results;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// State machine for COMMITTEE_REVIEW V1 (委員會審議). Submit opens a 3-member
/// parallel gateway (FINANCE + LEGAL + PROCUREMENT) with a QUORUM threshold of 2
/// (門檻 2/3): any 2 approvals pass, the remaining slot auto-skips; any reject
/// fails the case. Threshold variant of the 並簽 reference (CONTRACT_REVIEW).
/// </summary>
public sealed class COMMITTEE_REVIEW_V1_Service(
    ICOMMITTEE_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IClock clock,
    INotifyDispatcher notify,
    IPrincipalDirectory directory)
{
    public const string FlowCode = "COMMITTEE_REVIEW";
    public const int FlowVersion = 1;

    // BPMN node ids — MUST match the shipped .bpmn.xml.
    public const string GatewayNodeId = "gw_committee";
    public const string FinanceNodeId = "task_finance";
    public const string LegalNodeId = "task_legal";
    public const string ProcurementNodeId = "task_procurement";
    public const string FinanceRole = "FINANCE";
    public const string LegalRole = "LEGAL";
    public const string ProcurementRole = "PROCUREMENT";
    public const int Threshold = 2; // 2 of 3 (門檻)

    public sealed record SubmitInput(Guid SubmitterUserId, string Title, decimal Amount, string Currency, string Purpose);

    public async Task<COMMITTEE_REVIEW_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) throw Invalid(nameof(input.Title), "title is required");
        if (string.IsNullOrWhiteSpace(input.Purpose)) throw Invalid(nameof(input.Purpose), "purpose is required");
        if (input.Amount < 0) throw Invalid(nameof(input.Amount), "amount must be >= 0");

        var now = clock.UtcNow;
        var c = new COMMITTEE_REVIEW_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            Title = input.Title.Trim(),
            Amount = input.Amount,
            Currency = string.IsNullOrWhiteSpace(input.Currency) ? "NTD" : input.Currency.Trim(),
            Purpose = input.Purpose.Trim(),
            Status = COMMITTEE_REVIEW_V1_CaseStatus.PendingCommittee,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await parallel.OpenAsync(FlowCode, FlowVersion, c.Id, GatewayNodeId,
            new List<SlotSpec>
            {
                new(FinanceNodeId, FinanceRole, null),
                new(LegalNodeId, LegalRole, null),
                new(ProcurementNodeId, ProcurementRole, null),
            },
            threshold: Threshold, ct);

        await NotifySubmittedAsync(c, ct);
        await NotifyApproversAsync(c, new[] { FinanceRole, LegalRole, ProcurementRole }, ct);
        return c;
    }

    public async Task<COMMITTEE_REVIEW_V1_Case> DecideAsync(Guid caseId, Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(COMMITTEE_REVIEW_V1_Case), caseId);
        if (c.Status != COMMITTEE_REVIEW_V1_CaseStatus.PendingCommittee)
            throw new ConflictException("case is not pending committee review");

        var result = await parallel.DecideAsync(slotId, actorUserId, approve, comment, ct);

        c.LastActivityAt = clock.UtcNow;
        var resolved = ParallelGroupStatus.Open;
        if (result.GroupStatus == ParallelGroupStatus.Approved)
        {
            c.Status = COMMITTEE_REVIEW_V1_CaseStatus.Completed;
            c.CompletedAt = clock.UtcNow;
            resolved = ParallelGroupStatus.Approved;
        }
        else if (result.GroupStatus == ParallelGroupStatus.Rejected)
        {
            c.Status = COMMITTEE_REVIEW_V1_CaseStatus.Rejected;
            resolved = ParallelGroupStatus.Rejected;
        }

        await store.SaveChangesAsync(ct);

        if (resolved == ParallelGroupStatus.Approved) await NotifyResultAsync(c, approved: true, ct);
        else if (resolved == ParallelGroupStatus.Rejected) await NotifyResultAsync(c, approved: false, ct);
        return c;
    }

    // ── notifications ───────────────────────────────────────────────────────
    private string CaseUrl(COMMITTEE_REVIEW_V1_Case c) => $"/cases/committee-review/{c.Id}";

    private async Task NotifySubmittedAsync(COMMITTEE_REVIEW_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderSubmitted(c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_submitted",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private async Task NotifyApproversAsync(COMMITTEE_REVIEW_V1_Case c, IReadOnlyList<string> roleCodes, CancellationToken ct)
    {
        var userIds = new List<Guid>();
        foreach (var role in roleCodes)
            userIds.AddRange(await directory.GetUsersInRoleAsync(role, ct));
        userIds = userIds.Distinct().ToList();
        if (userIds.Count == 0) return;

        var lookups = await directory.GetManyAsync(userIds.Append(c.SubmitterUserId).ToArray(), ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? c.SubmitterUserId.ToString()[..8];
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderParallelAssign(applicant, c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_parallel_assign",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: userIds.Select(id => new NotifyRecipient(id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyResultAsync(COMMITTEE_REVIEW_V1_Case c, bool approved, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = approved
            ? COMMITTEE_REVIEW_V1_NotificationTemplates.RenderCompleted(c.Title, CaseUrl(c))
            : COMMITTEE_REVIEW_V1_NotificationTemplates.RenderRejected(c.Title, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_{(approved ? "completed" : "rejected")}",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> Ctx(COMMITTEE_REVIEW_V1_Case c)
        => new Dictionary<string, string?> { ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode };

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}

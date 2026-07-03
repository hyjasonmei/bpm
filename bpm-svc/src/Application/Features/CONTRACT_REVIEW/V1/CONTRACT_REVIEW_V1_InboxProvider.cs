using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Application.Parallel;

namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Unified-inbox provider for CONTRACT_REVIEW V1. "Mine" = cases I submitted.
/// "Pending" comes from the shared parallel primitive: any Open slot assigned to
/// one of my roles (or me) surfaces its case — so both LEGAL and FINANCE see the
/// same case concurrently, and it drops out the moment their slot resolves.
/// </summary>
public sealed class CONTRACT_REVIEW_V1_InboxProvider(
    ICONTRACT_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => CONTRACT_REVIEW_V1_Service.FlowCode;
    public int FlowVersion => CONTRACT_REVIEW_V1_Service.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"合約審查：{c.Title}",
            Status: ZhStatus(c.Status),
            Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/contract-review/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var roles = await directory.GetRoleCodesForUserAsync(userId, ct);
        var slots = await parallel.FindPendingForUserAsync(FlowCode, userId, roles, ct);
        if (slots.Count == 0) return Array.Empty<InboxRow>();

        var caseIds = slots.Select(s => s.CaseId).Distinct().ToArray();
        var cases = await store.FindByIdsAsync(caseIds, ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"合約審查（並簽）：{c.Title} · {c.Counterparty}",
            Status: "待你並簽",
            Lifecycle: InboxLifecycle.Open,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/contract-review/{c.Id}")).ToList();
    }

    private static string ZhStatus(Domain.Features.CONTRACT_REVIEW.V1.CONTRACT_REVIEW_V1_CaseStatus s) => s switch
    {
        Domain.Features.CONTRACT_REVIEW.V1.CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview => "並簽審查中",
        Domain.Features.CONTRACT_REVIEW.V1.CONTRACT_REVIEW_V1_CaseStatus.Completed => "已完成",
        Domain.Features.CONTRACT_REVIEW.V1.CONTRACT_REVIEW_V1_CaseStatus.Rejected => "已退件",
        _ => s.ToString(),
    };
}

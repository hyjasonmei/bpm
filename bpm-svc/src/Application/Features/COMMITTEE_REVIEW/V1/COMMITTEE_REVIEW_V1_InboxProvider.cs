using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Application.Parallel;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Unified-inbox provider for COMMITTEE_REVIEW V1. Pending comes from the shared
/// parallel primitive: every committee member (財務/法務/採購) sees the case
/// concurrently; it drops out the moment their slot resolves (or the quorum is met).
/// </summary>
public sealed class COMMITTEE_REVIEW_V1_InboxProvider(
    ICOMMITTEE_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IPrincipalDirectory directory) : ITypedInboxProvider
{
    public string FlowCode => COMMITTEE_REVIEW_V1_Service.FlowCode;
    public int FlowVersion => COMMITTEE_REVIEW_V1_Service.FlowVersion;

    public async Task<IReadOnlyList<InboxRow>> GetMineAsync(Guid userId, CancellationToken ct)
    {
        var cases = await store.FindMineAsync(userId, ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"委員會審議：{c.Title}",
            Status: ZhStatus(c.Status),
            Lifecycle: InboxLifecycle.FromStatusName(c.Status.ToString()),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/committee-review/{c.Id}")).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var roles = await directory.GetRoleCodesForUserAsync(userId, ct);
        var slots = await parallel.FindPendingForUserAsync(FlowCode, userId, roles, ct);
        if (slots.Count == 0) return Array.Empty<InboxRow>();

        var cases = await store.FindByIdsAsync(slots.Select(s => s.CaseId).Distinct().ToArray(), ct);
        return cases.Select(c => new InboxRow(
            CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: $"委員會審議（門檻 2/3）：{c.Title}",
            Status: "待你審議",
            Lifecycle: InboxLifecycle.Open,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/committee-review/{c.Id}")).ToList();
    }

    private static string ZhStatus(Domain.Features.COMMITTEE_REVIEW.V1.COMMITTEE_REVIEW_V1_CaseStatus s) => s switch
    {
        Domain.Features.COMMITTEE_REVIEW.V1.COMMITTEE_REVIEW_V1_CaseStatus.PendingCommittee => "委員會審議中",
        Domain.Features.COMMITTEE_REVIEW.V1.COMMITTEE_REVIEW_V1_CaseStatus.Completed => "已通過",
        Domain.Features.COMMITTEE_REVIEW.V1.COMMITTEE_REVIEW_V1_CaseStatus.Rejected => "已退件",
        _ => s.ToString(),
    };
}

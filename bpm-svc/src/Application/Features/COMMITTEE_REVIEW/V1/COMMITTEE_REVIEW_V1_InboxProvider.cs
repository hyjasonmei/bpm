using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Unified-inbox provider for COMMITTEE_REVIEW V1.
///
/// "Mine" = cases I submitted. "Pending my action" is the union of three queues:
///   1. the shared parallel primitive's open slots for one of my committee roles
///      (FINANCE / PROCUREMENT / IT_COMMITTEE) — but only while the case is still
///      PendingParallelReview, so a withdrawn / advanced case's orphan slots
///      never leak;
///   2. cases in PendingCeo when I hold CEO (最終裁決 queue);
///   3. my own cases sent back for revision (ResubmitRequired).
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
        return cases.Select(c => Row(c, $"委員會審議：{c.CaseTitle}", ZhStatus(c.Status),
            InboxLifecycle.FromStatusName(c.Status.ToString()))).ToList();
    }

    public async Task<IReadOnlyList<InboxRow>> GetPendingAsync(Guid userId, CancellationToken ct)
    {
        var roles = await directory.GetRoleCodesForUserAsync(userId, ct);
        var rows = new List<InboxRow>();
        var seen = new HashSet<Guid>();

        // 1. Concurrent 並簽 slots (only surface while the case is still in review).
        var slots = await parallel.FindPendingForUserAsync(FlowCode, userId, roles, ct);
        var slotCaseIds = slots.Select(s => s.CaseId).Distinct().ToArray();
        if (slotCaseIds.Length > 0)
        {
            foreach (var c in await store.FindByIdsAsync(slotCaseIds, ct))
            {
                if (c.Status != COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview) continue;
                if (seen.Add(c.Id))
                    rows.Add(Row(c, $"委員會審議（並簽）：{c.CaseTitle}", "待你審議", InboxLifecycle.Open));
            }
        }

        // 2. CEO 最終裁決 queue.
        if (roles.Contains(COMMITTEE_REVIEW_V1_Service.CeoRole))
        {
            foreach (var c in await store.FindByStatusAsync(COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo, ct))
                if (seen.Add(c.Id))
                    rows.Add(Row(c, $"委員會審議（待裁決）：{c.CaseTitle}", "待你最終裁決", InboxLifecycle.Open));
        }

        // 3. My own cases sent back for revision.
        foreach (var c in await store.FindByStatusAsync(COMMITTEE_REVIEW_V1_CaseStatus.ResubmitRequired, ct))
            if (c.SubmitterUserId == userId && seen.Add(c.Id))
                rows.Add(Row(c, $"委員會審議（待修改）：{c.CaseTitle}", "待你修改重送", InboxLifecycle.Open));

        return rows;
    }

    private InboxRow Row(COMMITTEE_REVIEW_V1_Case c, string title, string status, string lifecycle) =>
        new(CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: title, Status: status, Lifecycle: lifecycle,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/committee_review/{c.Id}");

    private static string ZhStatus(COMMITTEE_REVIEW_V1_CaseStatus s) => s switch
    {
        COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview => "委員並簽中",
        COMMITTEE_REVIEW_V1_CaseStatus.ResubmitRequired => "待修改重送",
        COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo => "待執行長裁決",
        COMMITTEE_REVIEW_V1_CaseStatus.Completed => "已核定",
        COMMITTEE_REVIEW_V1_CaseStatus.Rejected => "已否決",
        COMMITTEE_REVIEW_V1_CaseStatus.Cancelled => "已撤回",
        _ => s.ToString(),
    };
}

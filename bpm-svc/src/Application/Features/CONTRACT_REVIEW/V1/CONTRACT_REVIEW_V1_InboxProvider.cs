using Bpm.Application.Common.Directory;
using Bpm.Application.Inbox;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;

namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Unified-inbox provider for CONTRACT_REVIEW V1.
///
/// "Mine" = cases I submitted. "Pending my action" is the union of three queues:
///   1. the shared parallel primitive's open slots for one of my roles (LEGAL /
///      FINANCE) — but only while the case is still PendingParallelReview, so a
///      withdrawn / advanced case's orphan slots never leak;
///   2. cases in PendingLegalManager when I hold LEGAL_MANAGER (定案歸檔 queue);
///   3. my own cases sent back for revision (ResubmitRequired).
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
        return cases.Select(c => Row(c, $"合約審查：{c.ContractSubject}", ZhStatus(c.Status),
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
                if (c.Status != CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview) continue;
                if (seen.Add(c.Id))
                    rows.Add(Row(c, $"合約審查（並簽）：{c.ContractSubject} · {c.CounterpartyName}", "待你並簽", InboxLifecycle.Open));
            }
        }

        // 2. LEGAL_MANAGER 定案歸檔 queue.
        if (roles.Contains(CONTRACT_REVIEW_V1_Service.LegalManagerRole))
        {
            foreach (var c in await store.FindByStatusAsync(CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager, ct))
                if (seen.Add(c.Id))
                    rows.Add(Row(c, $"合約定案歸檔：{c.ContractSubject} · {c.CounterpartyName}", "待你定案歸檔", InboxLifecycle.Open));
        }

        // 3. My own cases sent back for revision.
        foreach (var c in await store.FindByStatusAsync(CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired, ct))
            if (c.SubmitterUserId == userId && seen.Add(c.Id))
                rows.Add(Row(c, $"合約審查（待修改）：{c.ContractSubject}", "待你修改重送", InboxLifecycle.Open));

        return rows;
    }

    private InboxRow Row(CONTRACT_REVIEW_V1_Case c, string title, string status, string lifecycle) =>
        new(CaseId: c.Id, FlowCode: FlowCode, FlowVersion: FlowVersion,
            Title: title, Status: status, Lifecycle: lifecycle,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt,
            DetailUrl: $"/cases/contract_review/{c.Id}");

    private static string ZhStatus(CONTRACT_REVIEW_V1_CaseStatus s) => s switch
    {
        CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview => "並簽審查中",
        CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired => "待修改重送",
        CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager => "待法務主管定案",
        CONTRACT_REVIEW_V1_CaseStatus.Completed => "已完成",
        CONTRACT_REVIEW_V1_CaseStatus.Cancelled => "已撤回",
        _ => s.ToString(),
    };
}

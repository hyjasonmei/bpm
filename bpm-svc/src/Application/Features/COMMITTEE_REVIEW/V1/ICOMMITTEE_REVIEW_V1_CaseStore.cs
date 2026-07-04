using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Per-flow data-access port for the COMMITTEE_REVIEW V1 case table.
///
/// The concurrent-review "pending" queue is served by the shared parallel
/// primitive (open slots), so this store only needs mine + by-id + by-id-set +
/// by-status (the last one surfaces the single-approver CEO queue + the
/// submitter's ResubmitRequired queue for the inbox).
/// </summary>
public interface ICOMMITTEE_REVIEW_V1_CaseStore
{
    void Add(COMMITTEE_REVIEW_V1_Case @case);
    Task<COMMITTEE_REVIEW_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindByStatusAsync(COMMITTEE_REVIEW_V1_CaseStatus status, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

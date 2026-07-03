using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>Per-flow data-access port. Pending is served by the parallel primitive.</summary>
public interface ICOMMITTEE_REVIEW_V1_CaseStore
{
    void Add(COMMITTEE_REVIEW_V1_Case @case);
    Task<COMMITTEE_REVIEW_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

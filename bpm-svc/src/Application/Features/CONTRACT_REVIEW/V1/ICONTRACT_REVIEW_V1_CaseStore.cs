using Bpm.Domain.Features.CONTRACT_REVIEW.V1;

namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Per-flow data-access port for the CONTRACT_REVIEW V1 case table. "Pending"
/// is served by the shared parallel-approval primitive (slots), so this store
/// only needs mine + by-id + by-id-set.
/// </summary>
public interface ICONTRACT_REVIEW_V1_CaseStore
{
    void Add(CONTRACT_REVIEW_V1_Case @case);
    Task<CONTRACT_REVIEW_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<CONTRACT_REVIEW_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<CONTRACT_REVIEW_V1_Case>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

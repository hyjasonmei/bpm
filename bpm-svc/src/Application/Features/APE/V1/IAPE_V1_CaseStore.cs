using Bpm.Domain.Features.APE.V1;

namespace Bpm.Application.Features.APE.V1;

/// <summary>
/// Per-flow data access port for the APE V1 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/APE/V1/</c>.
/// </summary>
public interface IAPE_V1_CaseStore
{
    void Add(APE_V1_Case @case);
    Task<APE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<APE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<APE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

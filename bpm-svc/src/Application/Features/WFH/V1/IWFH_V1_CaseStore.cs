using Bpm.Domain.Features.WFH.V1;

namespace Bpm.Application.Features.WFH.V1;

/// <summary>
/// Per-flow data access port for the WFH V1 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V1/</c>.
/// </summary>
public interface IWFH_V1_CaseStore
{
    void Add(WFH_V1_Case @case);
    Task<WFH_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

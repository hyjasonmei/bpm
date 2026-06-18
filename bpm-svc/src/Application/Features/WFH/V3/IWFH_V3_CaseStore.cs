using Bpm.Domain.Features.WFH.V3;

namespace Bpm.Application.Features.WFH.V3;

/// <summary>
/// Per-flow data access port for the WFH V3 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V3/</c>.
/// </summary>
public interface IWFH_V3_CaseStore
{
    void Add(WFH_V3_Case @case);
    Task<WFH_V3_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V3_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V3_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

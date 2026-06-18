using Bpm.Domain.Features.WFH.V2;

namespace Bpm.Application.Features.WFH.V2;

/// <summary>
/// Per-flow data access port for the WFH V2 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V2/</c>.
/// </summary>
public interface IWFH_V2_CaseStore
{
    void Add(WFH_V2_Case @case);
    Task<WFH_V2_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V2_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V2_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

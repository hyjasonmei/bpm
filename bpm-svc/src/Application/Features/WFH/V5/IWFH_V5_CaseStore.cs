using Bpm.Domain.Features.WFH.V5;

namespace Bpm.Application.Features.WFH.V5;

/// <summary>
/// Per-flow data access port for the WFH V5 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V5/</c>.
/// </summary>
public interface IWFH_V5_CaseStore
{
    void Add(WFH_V5_Case @case);
    Task<WFH_V5_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V5_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V5_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

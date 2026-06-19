using Bpm.Domain.Features.WFH.V6;

namespace Bpm.Application.Features.WFH.V6;

/// <summary>
/// Per-flow data access port for the WFH V6 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V6/</c>.
/// </summary>
public interface IWFH_V6_CaseStore
{
    void Add(WFH_V6_Case @case);
    Task<WFH_V6_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V6_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V6_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

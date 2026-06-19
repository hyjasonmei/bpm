using Bpm.Domain.Features.WFH.V4;

namespace Bpm.Application.Features.WFH.V4;

/// <summary>
/// Per-flow data access port for the WFH V4 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/WFH/V4/</c>.
/// </summary>
public interface IWFH_V4_CaseStore
{
    void Add(WFH_V4_Case @case);
    Task<WFH_V4_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V4_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WFH_V4_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

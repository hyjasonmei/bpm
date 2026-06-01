using Bpm.Domain.Features.EOB.V1;

namespace Bpm.Application.Features.EOB.V1;

/// <summary>Per-flow data access port for the EOB V1 case table.</summary>
public interface IEOB_V1_CaseStore
{
    void Add(EOB_V1_Case @case);
    Task<EOB_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<EOB_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<EOB_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

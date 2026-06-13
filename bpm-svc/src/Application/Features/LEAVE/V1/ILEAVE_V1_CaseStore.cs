using Bpm.Domain.Features.LEAVE.V1;

namespace Bpm.Application.Features.LEAVE.V1;

/// <summary>
/// Per-flow data access port for the LEAVE V1 case table. Application
/// can't reference Persistence (cyclic dep); the EF impl lives in
/// <c>Persistence/Features/LEAVE/V1/</c>.
/// </summary>
public interface ILEAVE_V1_CaseStore
{
    void Add(LEAVE_V1_Case @case);
    Task<LEAVE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<LEAVE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<LEAVE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

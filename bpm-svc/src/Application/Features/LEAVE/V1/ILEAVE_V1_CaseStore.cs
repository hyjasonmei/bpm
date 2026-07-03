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
    // Shared-role-queue: a case is pending for the user if they're the direct
    // assignee OR they hold the role the case is pending on (myRoleCodes).
    Task<IReadOnlyList<LEAVE_V1_Case>> FindPendingAsync(Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

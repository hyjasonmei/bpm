using Bpm.Domain.Features.TEO.V1;

namespace Bpm.Application.Features.TEO.V1;

/// <summary>Per-flow data access port for the TEO V1 case table.</summary>
public interface ITEO_V1_CaseStore
{
    void Add(TEO_V1_Case @case);
    Task<TEO_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<TEO_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    // Shared-role-queue: a case is pending for the user if they're the direct
    // assignee OR they hold the role the case is pending on (myRoleCodes).
    Task<IReadOnlyList<TEO_V1_Case>> FindPendingAsync(Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

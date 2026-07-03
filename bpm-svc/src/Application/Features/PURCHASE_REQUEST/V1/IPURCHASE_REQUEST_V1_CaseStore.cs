using Bpm.Domain.Features.PURCHASE_REQUEST.V1;

namespace Bpm.Application.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Per-flow data access port for the PURCHASE_REQUEST V1 case table.
/// Application can't reference Persistence (cyclic dep), so chef ships
/// this interface alongside the state machine and the EF impl lives in
/// <c>Persistence/Features/PURCHASE_REQUEST/V1/</c>.
/// </summary>
public interface IPURCHASE_REQUEST_V1_CaseStore
{
    void Add(PURCHASE_REQUEST_V1_Case @case);

    Task<PURCHASE_REQUEST_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);

    Task<IReadOnlyList<PURCHASE_REQUEST_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);

    // Shared-role-queue: a case is pending for the user if they're the direct
    // assignee OR they hold the role the case is pending on (myRoleCodes).
    Task<IReadOnlyList<PURCHASE_REQUEST_V1_Case>> FindPendingAsync(Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

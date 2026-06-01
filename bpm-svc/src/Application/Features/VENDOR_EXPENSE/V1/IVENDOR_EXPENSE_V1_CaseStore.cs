using Bpm.Domain.Features.VENDOR_EXPENSE.V1;

namespace Bpm.Application.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Per-flow data access port for the VENDOR_EXPENSE V1 case table.
/// Application can't reference Persistence (cyclic dep), so chef ships
/// this interface alongside the state machine and the EF impl lives in
/// <c>Persistence/Features/VENDOR_EXPENSE/V1/</c>.
/// </summary>
public interface IVENDOR_EXPENSE_V1_CaseStore
{
    void Add(VENDOR_EXPENSE_V1_Case @case);

    Task<VENDOR_EXPENSE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);

    Task<IReadOnlyList<VENDOR_EXPENSE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);

    Task<IReadOnlyList<VENDOR_EXPENSE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

using Bpm.Domain.Features.OVERTIME.V1;

namespace Bpm.Application.Features.OVERTIME.V1;

/// <summary>
/// Per-flow data access port for the OVERTIME V1 case table. Application can't
/// reference Persistence (cyclic dep), so chef ships this interface alongside the
/// state machine and the EF impl lives in
/// <c>Persistence/Features/OVERTIME/V1/</c>.
/// </summary>
public interface IOVERTIME_V1_CaseStore
{
    void Add(OVERTIME_V1_Case @case);

    Task<OVERTIME_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);

    Task<IReadOnlyList<OVERTIME_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);

    // Shared-role-queue: a case is pending for the user if they're the direct
    // assignee OR they hold the role the case is pending on (myRoleCodes).
    Task<IReadOnlyList<OVERTIME_V1_Case>> FindPendingAsync(
        Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default);

    /// <summary>
    /// Sum of 預估時數 across the submitter's <see cref="OVERTIME_V1_CaseStatus.Completed"/>
    /// overtime cases whose 加班日期 falls in <c>[monthStart, nextMonthStart)</c>,
    /// excluding <paramref name="excludeCaseId"/>. Null hours count as 0. Backs the
    /// manager-approval gateway's 單月累計時數 evaluation.
    /// </summary>
    Task<decimal> SumCompletedHoursInMonthAsync(
        Guid submitterUserId, DateOnly monthStart, DateOnly nextMonthStart, Guid excludeCaseId,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

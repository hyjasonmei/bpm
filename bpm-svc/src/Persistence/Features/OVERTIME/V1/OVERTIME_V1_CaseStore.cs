using Bpm.Application.Features.OVERTIME.V1;
using Bpm.Domain.Features.OVERTIME.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.OVERTIME.V1;

/// <summary>
/// EF-backed impl of <see cref="IOVERTIME_V1_CaseStore"/>. Lives in Persistence
/// because it binds against <c>AppDbContext</c>; the interface (chef-owned) lives
/// in Application alongside the service that consumes it.
/// </summary>
public sealed class OVERTIME_V1_CaseStore(AppDbContext db) : IOVERTIME_V1_CaseStore
{
    public void Add(OVERTIME_V1_Case @case)
        => db.Set<OVERTIME_V1_Case>().Add(@case);

    public Task<OVERTIME_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<OVERTIME_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<OVERTIME_V1_Case>> FindMineAsync(
        Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<OVERTIME_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OVERTIME_V1_Case>> FindPendingAsync(
        Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default)
    {
        var roles = myRoleCodes.ToArray();
        return await db.Set<OVERTIME_V1_Case>().AsNoTracking()
            .Where(c => (c.Status == OVERTIME_V1_CaseStatus.PendingManager
                         || c.Status == OVERTIME_V1_CaseStatus.PendingHr)
                        && (c.CurrentAssigneeUserId == assigneeUserId
                            || (c.CurrentAssigneeRoleCode != null && roles.Contains(c.CurrentAssigneeRoleCode))))
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
    }

    public async Task<decimal> SumCompletedHoursInMonthAsync(
        Guid submitterUserId, DateOnly monthStart, DateOnly nextMonthStart, Guid excludeCaseId,
        CancellationToken ct = default)
    {
        // Fetch the (small) per-user-per-month hour set and sum in memory so the
        // nullable-decimal aggregate stays provider-agnostic (SQLite / Postgres).
        var hours = await db.Set<OVERTIME_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId
                        && c.Status == OVERTIME_V1_CaseStatus.Completed
                        && c.Id != excludeCaseId
                        && c.OvertimeDate >= monthStart
                        && c.OvertimeDate < nextMonthStart)
            .Select(c => c.EstimatedHours)
            .ToListAsync(ct);
        return hours.Sum(h => h ?? 0m);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

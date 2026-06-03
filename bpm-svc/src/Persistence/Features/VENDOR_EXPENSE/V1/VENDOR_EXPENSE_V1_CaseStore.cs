using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// EF-backed impl of <see cref="IVENDOR_EXPENSE_V1_CaseStore"/>.
/// Lives in Persistence because it binds against <c>AppDbContext</c>;
/// the interface (chef-owned) lives in Application alongside the
/// service that consumes it.
/// </summary>
public sealed class VENDOR_EXPENSE_V1_CaseStore(AppDbContext db) : IVENDOR_EXPENSE_V1_CaseStore
{
    public void Add(VENDOR_EXPENSE_V1_Case @case)
        => db.Set<VENDOR_EXPENSE_V1_Case>().Add(@case);

    public Task<VENDOR_EXPENSE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<VENDOR_EXPENSE_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<VENDOR_EXPENSE_V1_Case>> FindMineAsync(
        Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<VENDOR_EXPENSE_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VENDOR_EXPENSE_V1_Case>> FindPendingAsync(
        Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<VENDOR_EXPENSE_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != VENDOR_EXPENSE_V1_CaseStatus.Completed
                        && c.Status != VENDOR_EXPENSE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

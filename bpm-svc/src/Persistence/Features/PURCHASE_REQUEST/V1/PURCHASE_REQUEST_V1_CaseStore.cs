using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// EF-backed impl of <see cref="IPURCHASE_REQUEST_V1_CaseStore"/>.
/// Lives in Persistence because it binds against <c>AppDbContext</c>;
/// the interface (chef-owned) lives in Application alongside the
/// service that consumes it.
/// </summary>
public sealed class PURCHASE_REQUEST_V1_CaseStore(AppDbContext db) : IPURCHASE_REQUEST_V1_CaseStore
{
    public void Add(PURCHASE_REQUEST_V1_Case @case)
        => db.Set<PURCHASE_REQUEST_V1_Case>().Add(@case);

    public Task<PURCHASE_REQUEST_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<PURCHASE_REQUEST_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<PURCHASE_REQUEST_V1_Case>> FindMineAsync(
        Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<PURCHASE_REQUEST_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PURCHASE_REQUEST_V1_Case>> FindPendingAsync(
        Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<PURCHASE_REQUEST_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != PURCHASE_REQUEST_V1_CaseStatus.Completed)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

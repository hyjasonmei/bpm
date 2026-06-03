using Bpm.Application.Features.TRQ.V1;
using Bpm.Domain.Features.TRQ.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.TRQ.V1;

/// <summary>
/// EF-backed impl of <see cref="ITRQ_V1_CaseStore"/>. Lives in
/// Persistence because it binds against <c>AppDbContext</c>; the
/// interface (chef-owned) lives in Application alongside the service
/// that consumes it.
/// </summary>
public sealed class TRQ_V1_CaseStore(AppDbContext db) : ITRQ_V1_CaseStore
{
    public void Add(TRQ_V1_Case @case)
        => db.Set<TRQ_V1_Case>().Add(@case);

    public Task<TRQ_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<TRQ_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<TRQ_V1_Case>> FindMineAsync(
        Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<TRQ_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TRQ_V1_Case>> FindPendingAsync(
        Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<TRQ_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != TRQ_V1_CaseStatus.Completed
                        && c.Status != TRQ_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}

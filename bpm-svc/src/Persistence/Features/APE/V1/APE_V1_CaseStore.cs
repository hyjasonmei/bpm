using Bpm.Application.Features.APE.V1;
using Bpm.Domain.Features.APE.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.APE.V1;

/// <summary>EF-backed impl of <see cref="IAPE_V1_CaseStore"/>.</summary>
public sealed class APE_V1_CaseStore(AppDbContext db) : IAPE_V1_CaseStore
{
    public void Add(APE_V1_Case @case) => db.Set<APE_V1_Case>().Add(@case);

    public Task<APE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<APE_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<APE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<APE_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<APE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<APE_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != APE_V1_CaseStatus.Completed
                        && c.Status != APE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

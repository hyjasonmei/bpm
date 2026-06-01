using Bpm.Application.Features.EOB.V1;
using Bpm.Domain.Features.EOB.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.EOB.V1;

/// <summary>EF-backed impl of <see cref="IEOB_V1_CaseStore"/>.</summary>
public sealed class EOB_V1_CaseStore(AppDbContext db) : IEOB_V1_CaseStore
{
    public void Add(EOB_V1_Case @case) => db.Set<EOB_V1_Case>().Add(@case);

    public Task<EOB_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<EOB_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<EOB_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<EOB_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<EOB_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<EOB_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId && c.Status != EOB_V1_CaseStatus.Completed)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

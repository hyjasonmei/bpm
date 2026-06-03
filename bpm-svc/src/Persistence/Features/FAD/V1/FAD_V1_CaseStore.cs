using Bpm.Application.Features.FAD.V1;
using Bpm.Domain.Features.FAD.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.FAD.V1;

/// <summary>EF-backed impl of <see cref="IFAD_V1_CaseStore"/>.</summary>
public sealed class FAD_V1_CaseStore(AppDbContext db) : IFAD_V1_CaseStore
{
    public void Add(FAD_V1_Case @case) => db.Set<FAD_V1_Case>().Add(@case);

    public Task<FAD_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<FAD_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<FAD_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<FAD_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<FAD_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<FAD_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId && c.Status != FAD_V1_CaseStatus.Completed && c.Status != FAD_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

using Bpm.Application.Features.ETM.V1;
using Bpm.Domain.Features.ETM.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.ETM.V1;

/// <summary>EF-backed impl of <see cref="IETM_V1_CaseStore"/>.</summary>
public sealed class ETM_V1_CaseStore(AppDbContext db) : IETM_V1_CaseStore
{
    public void Add(ETM_V1_Case @case) => db.Set<ETM_V1_Case>().Add(@case);

    public Task<ETM_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<ETM_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<ETM_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<ETM_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<ETM_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<ETM_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId && c.Status != ETM_V1_CaseStatus.Completed && c.Status != ETM_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

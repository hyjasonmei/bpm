using Bpm.Application.Features.WFH.V3;
using Bpm.Domain.Features.WFH.V3;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.WFH.V3;

/// <summary>EF-backed impl of <see cref="IWFH_V3_CaseStore"/>.</summary>
public sealed class WFH_V3_CaseStore(AppDbContext db) : IWFH_V3_CaseStore
{
    public void Add(WFH_V3_Case @case) => db.Set<WFH_V3_Case>().Add(@case);

    public Task<WFH_V3_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<WFH_V3_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<WFH_V3_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<WFH_V3_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<WFH_V3_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<WFH_V3_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != WFH_V3_CaseStatus.Completed
                        && c.Status != WFH_V3_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

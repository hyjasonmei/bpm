using Bpm.Application.Features.FAP.V1;
using Bpm.Domain.Features.FAP.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.FAP.V1;

/// <summary>EF-backed impl of <see cref="IFAP_V1_CaseStore"/>.</summary>
public sealed class FAP_V1_CaseStore(AppDbContext db) : IFAP_V1_CaseStore
{
    public void Add(FAP_V1_Case @case) => db.Set<FAP_V1_Case>().Add(@case);

    public Task<FAP_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<FAP_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<FAP_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<FAP_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<FAP_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<FAP_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId && c.Status != FAP_V1_CaseStatus.Completed)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

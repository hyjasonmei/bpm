using Bpm.Application.Features.TEO.V1;
using Bpm.Domain.Features.TEO.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.TEO.V1;

/// <summary>EF-backed impl of <see cref="ITEO_V1_CaseStore"/>.</summary>
public sealed class TEO_V1_CaseStore(AppDbContext db) : ITEO_V1_CaseStore
{
    public void Add(TEO_V1_Case @case) => db.Set<TEO_V1_Case>().Add(@case);

    public Task<TEO_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<TEO_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<TEO_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<TEO_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<TEO_V1_Case>> FindPendingAsync(Guid assigneeUserId, IReadOnlySet<string> myRoleCodes, CancellationToken ct = default)
    {
        var roles = myRoleCodes.ToArray();
        return await db.Set<TEO_V1_Case>().AsNoTracking()
            .Where(c => (c.CurrentAssigneeUserId == assigneeUserId
                         || (c.CurrentAssigneeRoleCode != null && roles.Contains(c.CurrentAssigneeRoleCode)))
                        && c.Status != TEO_V1_CaseStatus.Completed && c.Status != TEO_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

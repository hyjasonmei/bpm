using Bpm.Application.Features.LEAVE.V1;
using Bpm.Domain.Features.LEAVE.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.LEAVE.V1;

/// <summary>EF-backed impl of <see cref="ILEAVE_V1_CaseStore"/>.</summary>
public sealed class LEAVE_V1_CaseStore(AppDbContext db) : ILEAVE_V1_CaseStore
{
    public void Add(LEAVE_V1_Case @case) => db.Set<LEAVE_V1_Case>().Add(@case);

    public Task<LEAVE_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<LEAVE_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<LEAVE_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<LEAVE_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    // "Pending" = waiting on the assignee; LEAVE drops the three terminal
    // states (Completed / Rejected / Cancelled) out of every inbox.
    public async Task<IReadOnlyList<LEAVE_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default)
        => await db.Set<LEAVE_V1_Case>().AsNoTracking()
            .Where(c => c.CurrentAssigneeUserId == assigneeUserId
                        && c.Status != LEAVE_V1_CaseStatus.Completed
                        && c.Status != LEAVE_V1_CaseStatus.Rejected
                        && c.Status != LEAVE_V1_CaseStatus.Cancelled)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

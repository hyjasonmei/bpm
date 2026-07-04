using Bpm.Application.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.COMMITTEE_REVIEW.V1;

/// <summary>EF-backed impl of <see cref="ICOMMITTEE_REVIEW_V1_CaseStore"/>.</summary>
public sealed class COMMITTEE_REVIEW_V1_CaseStore(AppDbContext db) : ICOMMITTEE_REVIEW_V1_CaseStore
{
    public void Add(COMMITTEE_REVIEW_V1_Case @case) => db.Set<COMMITTEE_REVIEW_V1_Case>().Add(@case);

    public Task<COMMITTEE_REVIEW_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<COMMITTEE_REVIEW_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<COMMITTEE_REVIEW_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await db.Set<COMMITTEE_REVIEW_V1_Case>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<COMMITTEE_REVIEW_V1_Case>> FindByStatusAsync(COMMITTEE_REVIEW_V1_CaseStatus status, CancellationToken ct = default)
        => await db.Set<COMMITTEE_REVIEW_V1_Case>().AsNoTracking()
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

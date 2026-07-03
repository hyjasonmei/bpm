using Bpm.Application.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Features.CONTRACT_REVIEW.V1;

/// <summary>EF-backed impl of <see cref="ICONTRACT_REVIEW_V1_CaseStore"/>.</summary>
public sealed class CONTRACT_REVIEW_V1_CaseStore(AppDbContext db) : ICONTRACT_REVIEW_V1_CaseStore
{
    public void Add(CONTRACT_REVIEW_V1_Case @case) => db.Set<CONTRACT_REVIEW_V1_Case>().Add(@case);

    public Task<CONTRACT_REVIEW_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default)
        => db.Set<CONTRACT_REVIEW_V1_Case>().SingleOrDefaultAsync(c => c.Id == caseId, ct);

    public async Task<IReadOnlyList<CONTRACT_REVIEW_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default)
        => await db.Set<CONTRACT_REVIEW_V1_Case>().AsNoTracking()
            .Where(c => c.SubmitterUserId == submitterUserId)
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public async Task<IReadOnlyList<CONTRACT_REVIEW_V1_Case>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await db.Set<CONTRACT_REVIEW_V1_Case>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderByDescending(c => c.LastActivityAt).ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

using Bpm.Application.Delegation;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Delegation;

/// <summary>
/// Real delegation lookup over <c>Admin_Delegations</c> (via the SharedDelegation
/// mirror). Replaces <see cref="StubDelegationService"/>. A delegation is in
/// effect only when the delegate has ACCEPTED it (Status == Accepted), it's
/// <c>Active</c>, and <c>StartAt &lt;= now &lt;= EndAt</c>. Single-hop — transitive
/// chains are not followed in v1.
/// </summary>
public sealed class DelegationService(AppDbContext db) : IDelegationService
{
    private const int Accepted = (int)DelegationStatus.Accepted;

    public async Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default)
        => await db.SharedDelegations.AsNoTracking()
            .Where(d => d.DelegatorPrincipalId == principalUserId && d.Active && d.Status == Accepted && d.StartAt <= nowUtc && d.EndAt >= nowUtc)
            .OrderByDescending(d => d.StartAt)
            .Select(d => (Guid?)d.DelegateToUserId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetActiveDelegatorsAsync(Guid delegateUserId, DateTime nowUtc, CancellationToken ct = default)
        => await db.SharedDelegations.AsNoTracking()
            .Where(d => d.DelegateToUserId == delegateUserId && d.Active && d.Status == Accepted && d.StartAt <= nowUtc && d.EndAt >= nowUtc)
            .Select(d => d.DelegatorPrincipalId)
            .Distinct()
            .ToListAsync(ct);
}

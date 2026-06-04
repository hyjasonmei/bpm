namespace Bpm.Application.Delegation;

/// <summary>
/// Resolves whether a principal has an active delegation in effect at the given
/// moment. The runtime invokes this once per spawned Task to transform
/// <c>OriginalAssigneeUserId</c> → <c>ActualAssigneeUserId</c>.
///
/// V1 backing implementation is a no-op stub; the full feature ships under the
/// <c>add-delegation</c> change.
/// </summary>
public interface IDelegationService
{
    /// <summary>The user the given principal has currently delegated TO (active + in-range), or null.</summary>
    Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>The user ids that have currently delegated to <paramref name="delegateUserId"/>
    /// (i.e. the people this user may act on behalf of right now).</summary>
    Task<IReadOnlyList<Guid>> GetActiveDelegatorsAsync(Guid delegateUserId, DateTime nowUtc, CancellationToken ct = default);
}

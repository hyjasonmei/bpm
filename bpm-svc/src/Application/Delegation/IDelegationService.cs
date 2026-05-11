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
    Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default);
}

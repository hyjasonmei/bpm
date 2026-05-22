using Bpm.Application.Delegation;

namespace Bpm.Persistence.Delegation;

/// <summary>
/// V1 stub. Always returns <c>null</c> meaning "no active delegation".
/// </summary>
// TODO(add-delegation): replace with a Delegations table lookup keyed on
// (PrincipalUserId, ValidFrom..ValidTo, Status=Active).
public sealed class StubDelegationService : IDelegationService
{
    public Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);
}

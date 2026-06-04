using Bpm.Application.Delegation;

namespace Bpm.Persistence.Delegation;

/// <summary>
/// No-op delegation service ("never any delegation") — kept for tests / callers
/// that want the inert behaviour. Production DI now binds the real
/// <see cref="DelegationService"/>.
/// </summary>
public sealed class StubDelegationService : IDelegationService
{
    public Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default)
        => Task.FromResult<Guid?>(null);

    public Task<IReadOnlyList<Guid>> GetActiveDelegatorsAsync(Guid delegateUserId, DateTime nowUtc, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
}

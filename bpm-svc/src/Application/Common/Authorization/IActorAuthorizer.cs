using Bpm.Application.Common.Abstractions;
using Bpm.Application.Delegation;

namespace Bpm.Application.Common.Authorization;

/// <summary>
/// Shared decision-authorization seam for chef-cooked flows. A flow step is
/// assigned to a specific user (e.g. <c>ManagerUserId</c>); the caller may act on
/// it if they ARE that user, or if they are that user's currently-active
/// delegate. Flows MUST authorize step decisions through this instead of a raw
/// <c>if (c.XUserId != caller) throw</c>, so delegation is honored everywhere.
/// </summary>
public interface IActorAuthorizer
{
    Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default);
}

public sealed class ActorAuthorizer(IDelegationService delegation, IClock clock) : IActorAuthorizer
{
    public async Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default)
    {
        if (requiredUserId == callerUserId) return true;
        var delegate_ = await delegation.GetActiveDelegateAsync(requiredUserId, clock.UtcNow, ct);
        return delegate_ == callerUserId;
    }
}

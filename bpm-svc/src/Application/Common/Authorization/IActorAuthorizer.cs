using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Delegation;

namespace Bpm.Application.Common.Authorization;

/// <summary>
/// Shared decision-authorization seam for chef-cooked flows.
///
/// A flow step is assigned either to a specific user (e.g. <c>ManagerUserId</c> —
/// a manager / org-chart step) OR to a role (a shared-role-queue step, e.g.
/// finance / procurement). For a USER step the caller may act if they ARE that
/// user or their active delegate. For a ROLE step ANY holder of that role may
/// act (shared queue). Flows MUST authorize step decisions through this instead
/// of a raw <c>if (c.XUserId != caller) throw</c>, so delegation and role-queue
/// semantics are honored everywhere.
/// </summary>
public interface IActorAuthorizer
{
    /// <summary>User step: caller is the assigned user or their active delegate.</summary>
    Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default);

    /// <summary>
    /// Role-or-user step. Pass <paramref name="requiredUserId"/> for a user step,
    /// <paramref name="requiredRoleCode"/> for a shared-role-queue step (either may
    /// be null; at least one should be set). True if caller matches the user
    /// (or is their delegate), OR holds the required role.
    /// </summary>
    Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid callerUserId, CancellationToken ct = default);
}

public sealed class ActorAuthorizer(IDelegationService delegation, IClock clock, IPrincipalDirectory directory) : IActorAuthorizer
{
    public async Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default)
    {
        if (requiredUserId == callerUserId) return true;
        var delegate_ = await delegation.GetActiveDelegateAsync(requiredUserId, clock.UtcNow, ct);
        return delegate_ == callerUserId;
    }

    public async Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid callerUserId, CancellationToken ct = default)
    {
        if (requiredUserId is { } u && await CanActAsync(u, callerUserId, ct)) return true;
        if (!string.IsNullOrEmpty(requiredRoleCode))
            return (await directory.GetRoleCodesForUserAsync(callerUserId, ct)).Contains(requiredRoleCode);
        return false;
    }
}

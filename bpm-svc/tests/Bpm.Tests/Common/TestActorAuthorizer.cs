using Bpm.Application.Common.Authorization;

namespace Bpm.Tests.Common;

/// <summary>
/// Self-only authorizer for unit tests: allows an action exactly when the caller
/// IS the required actor (no delegation). This mirrors real authorization for the
/// no-delegation path the feature tests exercise — the happy-path actor passes,
/// while "wrong user / wrong manager forbidden" negative tests are still denied.
/// </summary>
internal sealed class TestActorAuthorizer : IActorAuthorizer
{
    // Optional caller -> role-codes map so role-queue steps can be exercised in
    // feature tests. Empty by default (self-only, backward compatible).
    private readonly IReadOnlyDictionary<Guid, IReadOnlySet<string>> _callerRoles;

    public TestActorAuthorizer(IReadOnlyDictionary<Guid, IReadOnlySet<string>>? callerRoles = null)
        => _callerRoles = callerRoles ?? new Dictionary<Guid, IReadOnlySet<string>>();

    public Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default)
        => Task.FromResult(requiredUserId == callerUserId);

    public Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid callerUserId, CancellationToken ct = default)
    {
        if (requiredUserId is { } u && u == callerUserId) return Task.FromResult(true);
        if (!string.IsNullOrEmpty(requiredRoleCode)
            && _callerRoles.TryGetValue(callerUserId, out var roles)
            && roles.Contains(requiredRoleCode))
            return Task.FromResult(true);
        return Task.FromResult(false);
    }
}

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
    public Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default)
        => Task.FromResult(requiredUserId == callerUserId);
}

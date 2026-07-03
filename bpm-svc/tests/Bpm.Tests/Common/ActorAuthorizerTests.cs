using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Delegation;
using Xunit;

namespace Bpm.Tests.Common;

public class ActorAuthorizerTests
{
    private static readonly Guid Assignee = Guid.NewGuid();
    private static readonly Guid Delegate = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid FinancePerson = Guid.NewGuid();
    private static readonly Guid NonFinance = Guid.NewGuid();

    private sealed class FakeDelegation(Guid principal, Guid? activeDelegate) : IDelegationService
    {
        public Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default)
            => Task.FromResult(principalUserId == principal ? activeDelegate : null);
        public Task<IReadOnlyList<Guid>> GetActiveDelegatorsAsync(Guid delegateUserId, DateTime nowUtc, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<Guid>)Array.Empty<Guid>());
    }

    private sealed class FakeDirectory(IReadOnlyDictionary<Guid, IReadOnlySet<string>> roles) : IPrincipalDirectory
    {
        public Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(roles.TryGetValue(userId, out var r) ? r : (IReadOnlySet<string>)new HashSet<string>());
        public Task<PrincipalInfo?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, PrincipalInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Guid?> FindFirstUserInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string roleName, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<Guid>)roles.Where(kv => kv.Value.Contains(roleName)).Select(kv => kv.Key).ToList());
    }

    private static ActorAuthorizer Build(Guid? activeDelegate = null, IReadOnlyDictionary<Guid, IReadOnlySet<string>>? roles = null)
        => new(new FakeDelegation(Assignee, activeDelegate), new StubClock(),
               new FakeDirectory(roles ?? new Dictionary<Guid, IReadOnlySet<string>>()));

    // ── user step (existing behavior — must not regress) ──────────────────

    [Fact]
    public async Task UserStep_self_allowed()
        => Assert.True(await Build().CanActAsync(Assignee, Assignee));

    [Fact]
    public async Task UserStep_active_delegate_allowed()
        => Assert.True(await Build(activeDelegate: Delegate).CanActAsync(Assignee, Delegate));

    [Fact]
    public async Task UserStep_other_denied()
        => Assert.False(await Build().CanActAsync(Assignee, Other));

    // ── role step (shared role queue) ─────────────────────────────────────

    [Fact]
    public async Task RoleStep_role_holder_allowed()
    {
        var roles = new Dictionary<Guid, IReadOnlySet<string>> { [FinancePerson] = new HashSet<string> { "FINANCE" } };
        Assert.True(await Build(roles: roles).CanActAsync(null, "FINANCE", FinancePerson));
    }

    [Fact]
    public async Task RoleStep_non_holder_denied()
    {
        var roles = new Dictionary<Guid, IReadOnlySet<string>> { [FinancePerson] = new HashSet<string> { "FINANCE" } };
        Assert.False(await Build(roles: roles).CanActAsync(null, "FINANCE", NonFinance));
    }

    [Fact]
    public async Task RoleStep_any_of_multiple_holders_allowed()
    {
        var roles = new Dictionary<Guid, IReadOnlySet<string>>
        {
            [FinancePerson] = new HashSet<string> { "FINANCE" },
            [Other] = new HashSet<string> { "FINANCE", "PROCUREMENT" },   // 一人多職
        };
        var auth = Build(roles: roles);
        Assert.True(await auth.CanActAsync(null, "FINANCE", FinancePerson));
        Assert.True(await auth.CanActAsync(null, "FINANCE", Other));        // second holder also passes
        Assert.True(await auth.CanActAsync(null, "PROCUREMENT", Other));    // multi-role person
    }

    [Fact]
    public async Task UserId_match_wins_even_without_role()
        => Assert.True(await Build().CanActAsync(Assignee, "FINANCE", Assignee));  // user step encoded with a role hint
}

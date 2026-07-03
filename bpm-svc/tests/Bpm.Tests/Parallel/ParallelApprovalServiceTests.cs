using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Parallel;
using Bpm.Domain.Parallel;
using Bpm.Persistence;
using Bpm.Persistence.Parallel;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Parallel;

/// <summary>
/// Unit tests for the shared parallel-approval primitive: join at threshold,
/// any-reject-rejects-all, skip-remaining, decision guards, authorization, and
/// the inbox pending query. Real EF (in-memory SQLite) + stub authorizer.
/// </summary>
public sealed class ParallelApprovalServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Bob   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Carol = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid Case  = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private const string Gw = "gw_review";

    public ParallelApprovalServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    // Authorizer that approves everyone unless a deny-set is given.
    private sealed class StubAuthorizer(HashSet<Guid>? allowed = null) : IActorAuthorizer
    {
        public Task<bool> CanActAsync(Guid requiredUserId, Guid callerUserId, CancellationToken ct = default)
            => Task.FromResult(allowed == null || allowed.Contains(callerUserId));
        public Task<bool> CanActAsync(Guid? requiredUserId, string? requiredRoleCode, Guid callerUserId, CancellationToken ct = default)
            => Task.FromResult(allowed == null || allowed.Contains(callerUserId));
    }

    private ParallelApprovalService NewService(AppDbContext db, HashSet<Guid>? allowed = null)
        => new(db, new StubClock(), new StubAuthorizer(allowed));

    private static IReadOnlyList<SlotSpec> RoleSlots(int n) =>
        Enumerable.Range(1, n).Select(i => new SlotSpec($"task_{i}", $"ROLE_{i}", null)).ToList();

    private async Task<ParallelApprovalGroup> OpenAsync(AppDbContext db, int n, int threshold)
        => await NewService(db).OpenAsync("CONTRACT_REVIEW", 1, Case, Gw, RoleSlots(n), threshold, default);

    [Fact]
    public async Task AllApproved_threshold_N_of_N_resolves_Approved()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 3, threshold: 3);
        var svc = NewService(db);

        foreach (var slot in g.Slots)
            await svc.DecideAsync(slot.Id, Alice, approve: true, "ok", default);

        var final = await svc.GetAsync(Case, Gw, default);
        Assert.Equal(ParallelGroupStatus.Approved, final!.Status);
        Assert.All(final.Slots, s => Assert.Equal(SlotDecision.Approved, s.Decision));
        Assert.NotNull(final.ResolvedAt);
    }

    [Fact]
    public async Task Threshold_M_of_N_resolves_and_skips_remaining()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 5, threshold: 3);
        var svc = NewService(db);

        for (var i = 0; i < 3; i++)
            await svc.DecideAsync(g.Slots[i].Id, Alice, approve: true, null, default);

        var final = await svc.GetAsync(Case, Gw, default);
        Assert.Equal(ParallelGroupStatus.Approved, final!.Status);
        Assert.Equal(3, final.Slots.Count(s => s.Decision == SlotDecision.Approved));
        Assert.Equal(2, final.Slots.Count(s => s.Decision == SlotDecision.Skipped));
    }

    [Fact]
    public async Task Any_reject_rejects_group_and_skips_rest()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 3, threshold: 3);
        var svc = NewService(db);

        await svc.DecideAsync(g.Slots[0].Id, Alice, approve: false, "no", default);

        var final = await svc.GetAsync(Case, Gw, default);
        Assert.Equal(ParallelGroupStatus.Rejected, final!.Status);
        Assert.Equal(SlotDecision.Rejected, final.Slots[0].Decision);
        Assert.Equal(2, final.Slots.Count(s => s.Decision == SlotDecision.Skipped));
    }

    [Fact]
    public async Task Decide_on_resolved_group_throws_Conflict()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 2, threshold: 1);
        var svc = NewService(db);
        await svc.DecideAsync(g.Slots[0].Id, Alice, approve: true, null, default); // resolves (1/2)

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.DecideAsync(g.Slots[1].Id, Bob, approve: true, null, default));
    }

    [Fact]
    public async Task Decide_twice_on_same_slot_throws_Conflict()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 3, threshold: 3);
        var svc = NewService(db);
        await svc.DecideAsync(g.Slots[0].Id, Alice, approve: true, null, default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.DecideAsync(g.Slots[0].Id, Alice, approve: true, null, default));
    }

    [Fact]
    public async Task Unauthorized_actor_throws_Forbidden()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 3, threshold: 3);
        var svc = NewService(db, allowed: new HashSet<Guid> { Alice }); // Carol not allowed

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            svc.DecideAsync(g.Slots[0].Id, Carol, approve: true, null, default));
    }

    [Fact]
    public async Task FindPendingForUser_matches_role_slots_only_while_Pending()
    {
        await using var db = new AppDbContext(_options);
        var g = await OpenAsync(db, 3, threshold: 3); // ROLE_1/2/3
        var svc = NewService(db);

        var pending = await svc.FindPendingForUserAsync("CONTRACT_REVIEW", Bob, new[] { "ROLE_2" }, default);
        Assert.Single(pending);
        Assert.Equal("task_2", pending[0].NodeId);
        Assert.Equal(Case, pending[0].CaseId);

        // After ROLE_2 acts, no longer pending for Bob.
        await svc.DecideAsync(pending[0].SlotId, Bob, approve: true, null, default);
        var after = await svc.FindPendingForUserAsync("CONTRACT_REVIEW", Bob, new[] { "ROLE_2" }, default);
        Assert.Empty(after);
    }
}

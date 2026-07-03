using Bpm.Application.Common.Authorization;
using Bpm.Application.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Parallel;
using Bpm.Persistence;
using Bpm.Persistence.Features.COMMITTEE_REVIEW.V1;
using Bpm.Persistence.Parallel;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Quorum flow: COMMITTEE_REVIEW opens 3 slots (FINANCE/LEGAL/PROCUREMENT) with a
/// threshold of 2 (門檻 2/3). Two approvals complete the case + skip the third;
/// any reject fails it.
/// </summary>
public sealed class COMMITTEE_REVIEW_V1_FlowTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private static readonly Guid Sam = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid A = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid B = Guid.Parse("22222222-0000-0000-0000-000000000003");

    public COMMITTEE_REVIEW_V1_FlowTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private sealed class AllowAll : IActorAuthorizer
    {
        public Task<bool> CanActAsync(Guid r, Guid c, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanActAsync(Guid? r, string? rc, Guid c, CancellationToken ct = default) => Task.FromResult(true);
    }
    private sealed class NoopNotify : Bpm.Application.Notifications.INotifyDispatcher
    {
        public Task DispatchAsync(Bpm.Application.Notifications.NotifyMessage m, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class StubDirectory : Bpm.Application.Common.Directory.IPrincipalDirectory
    {
        public Task<Bpm.Application.Common.Directory.PrincipalInfo?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Bpm.Application.Common.Directory.PrincipalInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>>(new Dictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>());
        public Task<Guid?> FindFirstUserInRoleAsync(string role, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string role, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
        public Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private COMMITTEE_REVIEW_V1_Service NewService(AppDbContext db)
        => new(new COMMITTEE_REVIEW_V1_CaseStore(db), new ParallelApprovalService(db, new StubClock(), new AllowAll()),
               new StubClock(), new NoopNotify(), new StubDirectory());

    private ParallelApprovalService NewParallel(AppDbContext db) => new(db, new StubClock(), new AllowAll());
    private static COMMITTEE_REVIEW_V1_Service.SubmitInput Input() => new(Sam, "Q3 預算追加", 300000m, "NTD", "行銷擴編");

    [Fact]
    public async Task Submit_opens_three_slots_threshold_two()
    {
        await using var db = new AppDbContext(_options);
        var c = await NewService(db).SubmitAsync(Input(), default);
        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.GatewayNodeId, default);
        Assert.Equal(3, g!.TotalSlots);
        Assert.Equal(2, g.Threshold);
    }

    [Fact]
    public async Task Two_of_three_approve_completes_and_skips_third()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.GatewayNodeId, default);
        var slots = g!.Slots.ToList();

        var after1 = await svc.DecideAsync(c.Id, slots[0].Id, A, approve: true, "ok", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingCommittee, after1.Status); // 1/2

        var after2 = await svc.DecideAsync(c.Id, slots[1].Id, B, approve: true, "ok", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Completed, after2.Status);        // 2/2 quorum met

        var final = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.GatewayNodeId, default);
        Assert.Equal(2, final!.Slots.Count(s => s.Decision == SlotDecision.Approved));
        Assert.Equal(1, final.Slots.Count(s => s.Decision == SlotDecision.Skipped)); // third auto-skipped
    }

    [Fact]
    public async Task Any_reject_fails_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.GatewayNodeId, default);

        var after = await svc.DecideAsync(c.Id, g!.Slots.First().Id, A, approve: false, "no", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Rejected, after.Status);
    }
}

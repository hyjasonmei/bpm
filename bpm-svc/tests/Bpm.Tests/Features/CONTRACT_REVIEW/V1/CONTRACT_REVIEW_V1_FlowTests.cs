using Bpm.Application.Common.Authorization;
using Bpm.Application.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Bpm.Persistence;
using Bpm.Persistence.Features.CONTRACT_REVIEW.V1;
using Bpm.Persistence.Parallel;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Flow-level tests: CONTRACT_REVIEW opens a LEGAL+FINANCE 並簽 gateway and
/// advances on the shared parallel primitive. Real EF + real ParallelApprovalService.
/// </summary>
public sealed class CONTRACT_REVIEW_V1_FlowTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private static readonly Guid Sam = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Lena = Guid.Parse("11111111-0000-0000-0000-000000000002"); // LEGAL
    private static readonly Guid Fred = Guid.Parse("11111111-0000-0000-0000-000000000003"); // FINANCE

    public CONTRACT_REVIEW_V1_FlowTests()
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
        public Task<Bpm.Application.Common.Directory.PrincipalInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Bpm.Application.Common.Directory.PrincipalInfo?>(null);
        public Task<IReadOnlyDictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>>(new Dictionary<Guid, Bpm.Application.Common.Directory.PrincipalInfo>());
        public Task<Guid?> FindFirstUserInRoleAsync(string role, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
        public Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string role, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
        public Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private CONTRACT_REVIEW_V1_Service NewService(AppDbContext db)
    {
        var parallel = new ParallelApprovalService(db, new StubClock(), new AllowAll());
        var store = new CONTRACT_REVIEW_V1_CaseStore(db);
        return new CONTRACT_REVIEW_V1_Service(store, parallel, new StubClock(), new NoopNotify(), new StubDirectory());
    }

    private static CONTRACT_REVIEW_V1_Service.SubmitInput Input() =>
        new(Sam, "ACME 供貨合約", "ACME Corp", 500000m, "NTD", null);

    [Fact]
    public async Task Submit_opens_two_pending_slots()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);

        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview, c.Status);
        var group = await new ParallelApprovalService(db, new StubClock(), new AllowAll())
            .GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayNodeId, default);
        Assert.NotNull(group);
        Assert.Equal(2, group!.TotalSlots);
        Assert.Equal(2, group.Threshold);
        Assert.Contains(group.Slots, s => s.AssigneeRoleCode == "LEGAL");
        Assert.Contains(group.Slots, s => s.AssigneeRoleCode == "FINANCE");
    }

    [Fact]
    public async Task Both_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var group = await NewParallel(db).GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayNodeId, default);
        var legal = group!.Slots.First(s => s.AssigneeRoleCode == "LEGAL");
        var finance = group.Slots.First(s => s.AssigneeRoleCode == "FINANCE");

        var afterLegal = await svc.DecideAsync(c.Id, legal.Id, Lena, approve: true, "法務OK", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview, afterLegal.Status); // still waiting on finance

        var afterFinance = await svc.DecideAsync(c.Id, finance.Id, Fred, approve: true, "財務OK", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Completed, afterFinance.Status);
        Assert.NotNull(afterFinance.CompletedAt);
    }

    [Fact]
    public async Task Any_reject_rejects_case_and_skips_other()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var group = await NewParallel(db).GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayNodeId, default);
        var legal = group!.Slots.First(s => s.AssigneeRoleCode == "LEGAL");

        var after = await svc.DecideAsync(c.Id, legal.Id, Lena, approve: false, "條款有問題", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Rejected, after.Status);

        var resolved = await NewParallel(db).GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayNodeId, default);
        Assert.Equal(Bpm.Domain.Parallel.SlotDecision.Rejected, resolved!.Slots.First(s => s.AssigneeRoleCode == "LEGAL").Decision);
        Assert.Equal(Bpm.Domain.Parallel.SlotDecision.Skipped, resolved.Slots.First(s => s.AssigneeRoleCode == "FINANCE").Decision);
    }

    private ParallelApprovalService NewParallel(AppDbContext db) => new(db, new StubClock(), new AllowAll());
}

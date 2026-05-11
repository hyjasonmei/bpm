using Bpm.Application.Process.Reporting;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Process.Reporting;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Persistence.Process.Reporting;

/// <summary>
/// PR-K5 §8 — reporting aggregator + cache wrapper. Tests run against an
/// in-memory SQLite seeded with a tiny fixture: two users + configurable
/// instances/tasks per scenario. The point is to pin down the math
/// (averages, percentiles, breach counting, bottleneck ranking) and the
/// cache-hit semantics — not to prove runtime integration (covered by
/// ProcessRuntimeE2EFixture).
/// </summary>
public sealed class ProcessReportingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private const string SpecCode = "LEAVE";
    private readonly Guid _initiatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _assigneeId  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public ProcessReportingServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Users.Add(new User { Id = _initiatorId, DisplayName = "init", Email = "init@e2e", FullName = "Init" });
        db.Users.Add(new User { Id = _assigneeId,  DisplayName = "asg",  Email = "asg@e2e",  FullName = "Asg"  });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task EmptyDb_returnsZeros_andEmptyAggregates()
    {
        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.Equal(0, report.TotalStarted);
        Assert.Equal(0, report.TotalCompleted);
        Assert.Equal(0, report.TotalCancelled);
        Assert.Equal(0, report.TotalRunning);
        Assert.Equal(0d, report.AvgCycleTimeHours);
        Assert.Equal(0d, report.P50CycleTimeHours);
        Assert.Equal(0d, report.P90CycleTimeHours);
        Assert.Equal(0, report.BreachCount);
        Assert.Equal(0d, report.BreachRate);
        Assert.Empty(report.Bottlenecks);
        Assert.Empty(report.AssigneeLoads);
        // Histogram is always 5 buckets, just all zero.
        Assert.Equal(5, report.CycleTimeHistogram.Count);
        Assert.All(report.CycleTimeHistogram, b => Assert.Equal(0, b.Count));
    }

    [Fact]
    public async Task SingleCompletedCase_computesAvgCycleTime()
    {
        // 4-hour cycle.
        await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-4), _clock.UtcNow);

        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.Equal(1, report.TotalStarted);
        Assert.Equal(1, report.TotalCompleted);
        Assert.Equal(4d, report.AvgCycleTimeHours, precision: 4);
        Assert.Equal(4d, report.P50CycleTimeHours, precision: 4);
        Assert.Equal(4d, report.P90CycleTimeHours, precision: 4);
    }

    [Fact]
    public async Task MultipleCases_computesP50_andP90_inHours()
    {
        // Cycle times: 1, 2, 3, 4, 10 hours.
        var hours = new[] { 1, 2, 3, 4, 10 };
        foreach (var h in hours)
            await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-h), _clock.UtcNow);

        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.Equal(5, report.TotalCompleted);
        // Linear interpolation: P50 of [1,2,3,4,10] = 3.0, P90 ≈ 7.6.
        Assert.Equal(3d, report.P50CycleTimeHours, precision: 4);
        Assert.Equal(7.6d, report.P90CycleTimeHours, precision: 4);
        Assert.Equal(4d, report.AvgCycleTimeHours, precision: 4);
    }

    [Fact]
    public async Task Bottleneck_identifiedByLongestNodeAvgWait()
    {
        // Three running instances; two slow tasks at "approval" (4h each)
        // and three fast tasks at "form_in" (1h each). Bottleneck should
        // surface "approval" first.
        var i1 = await SeedRunningInstanceAsync();
        var i2 = await SeedRunningInstanceAsync();
        var i3 = await SeedRunningInstanceAsync();
        await SeedCompletedTaskAsync(i1, "form_in",  hours: 1);
        await SeedCompletedTaskAsync(i2, "form_in",  hours: 1);
        await SeedCompletedTaskAsync(i3, "form_in",  hours: 1);
        await SeedCompletedTaskAsync(i1, "approval", hours: 4);
        await SeedCompletedTaskAsync(i2, "approval", hours: 4);

        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.NotEmpty(report.Bottlenecks);
        var top = report.Bottlenecks[0];
        Assert.Equal("approval", top.NodeId);
        Assert.Equal(4d, top.AvgWaitTimeHours, precision: 4);
        Assert.Equal(2, top.CompletedCount);
    }

    [Fact]
    public async Task BreachRate_countsPastDueCompletionsAndOpenTasks()
    {
        // 2 completed, of which 1 breached on completion (CompletedAt > DueAt).
        var inst1 = await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-3), _clock.UtcNow);
        var inst2 = await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-3), _clock.UtcNow);
        await SeedCompletedTaskAsync(inst1, "approval", hours: 1, dueAtOffsetHours: -2);
        await SeedCompletedTaskAsync(inst2, "approval", hours: 1, dueAtOffsetHours: 4);

        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.Equal(2, report.TotalCompleted);
        Assert.Equal(1, report.BreachCount);
        Assert.Equal(0.5d, report.BreachRate, precision: 4);
    }

    [Fact]
    public async Task RunningCases_countTowardsTotalRunning_butNotTotalStartedScopedByPeriod()
    {
        // Out-of-period start (60 days ago) but still Running.
        await SeedInstanceAsync(_clock.UtcNow.AddDays(-60), status: InstanceStatus.Running, completedAt: null);
        // In-period completed.
        await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-2), _clock.UtcNow);

        var report = await BuildService().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        // Only the in-period one was started inside the window.
        Assert.Equal(1, report.TotalStarted);
        Assert.Equal(1, report.TotalCompleted);
        // Both Running and Errored count; the old Running case is included.
        Assert.Equal(1, report.TotalRunning);
    }

    [Fact]
    public async Task Cache_servesSecondCallFromMemo_withoutInvokingInner()
    {
        await SeedCompletedInstanceAsync(_clock.UtcNow.AddHours(-2), _clock.UtcNow);

        var counter = new CountingReportingService(BuildService());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new CachedProcessReportingService(counter, cache);

        // Two consecutive calls — second should be served from cache.
        var first  = await cached.GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);
        var second = await cached.GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);
        Assert.Equal(1, counter.Calls);
        Assert.Same(first, second);

        // After removing the cache entry the inner runs again.
        cache.Remove($"reports:per-spec:default:{SpecCode}:{ReportPeriod.Last30Days}");
        await cached.GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);
        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public void Percentile_handlesEdgeCases()
    {
        // White-box check on the percentile helper — it's the most
        // failure-prone bit of the aggregator and worth pinning explicitly.
        Assert.Equal(0d, ProcessReportingService.Percentile(Array.Empty<double>(), 0.5));
        Assert.Equal(7d, ProcessReportingService.Percentile(new[] { 7d }, 0.9));
        Assert.Equal(2d, ProcessReportingService.Percentile(new[] { 1d, 2d, 3d }, 0.5), precision: 6);
    }

    // ===== seeding helpers =====

    private IProcessReportingService BuildService()
    {
        var db = new AppDbContext(_options);
        return new ProcessReportingService(db, _clock);
    }

    private async Task<Guid> SeedCompletedInstanceAsync(DateTime startedAt, DateTime completedAt)
        => await SeedInstanceAsync(startedAt, InstanceStatus.Completed, completedAt);

    private async Task<Guid> SeedRunningInstanceAsync()
        => await SeedInstanceAsync(_clock.UtcNow.AddHours(-1), InstanceStatus.Running, null);

    private async Task<Guid> SeedInstanceAsync(DateTime startedAt, InstanceStatus status, DateTime? completedAt)
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.ProcessInstances.Add(new ProcessInstance
        {
            Id = id,
            SpecCode = SpecCode,
            SpecVersion = 1,
            InitiatorUserId = _initiatorId,
            Status = status,
            StartedAt = startedAt,
            CompletedAt = status == InstanceStatus.Completed ? completedAt : null,
            CancelledAt = status == InstanceStatus.Cancelled ? completedAt : null,
            LastActivityAt = completedAt ?? startedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedCompletedTaskAsync(Guid instanceId, string nodeId, double hours, double dueAtOffsetHours = 24)
    {
        await using var db = new AppDbContext(_options);
        var createdAt = _clock.UtcNow.AddHours(-hours);
        var completedAt = _clock.UtcNow;
        var task = new ProcessTask
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = instanceId,
            NodeId = nodeId,
            NodeKind = NodeKind.UserTask,
            ActualAssigneeUserId = _assigneeId,
            Status = TaskStatus.Completed,
            CompletedAt = completedAt,
            DueAt = createdAt.AddHours(dueAtOffsetHours),
        };
        db.ProcessTasks.Add(task);
        await db.SaveChangesAsync();

        // The audit interceptor stamps CreatedAt = now on Add and locks it
        // out of subsequent Updates. To pin a back-dated CreatedAt for the
        // bottleneck math we use ExecuteUpdate, which bypasses the
        // interceptor entirely (it operates on tracked entries).
        var taskId = task.Id;
        await db.ProcessTasks.Where(x => x.Id == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, createdAt));
    }

    /// <summary>
    /// Counts how many times the wrapped reporting service is invoked.
    /// Lets the cache test prove the wrapper short-circuits inner calls.
    /// </summary>
    private sealed class CountingReportingService(IProcessReportingService inner) : IProcessReportingService
    {
        public int Calls { get; private set; }

        public Task<PerSpecReport> GetPerSpecReportAsync(string specCode, ReportPeriod period, CancellationToken ct = default)
        {
            Calls++;
            return inner.GetPerSpecReportAsync(specCode, period, ct);
        }
    }
}

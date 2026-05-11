using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Application.Process.Admin;
using Bpm.Application.Process.Reporting;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Process.Simulator;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Admin.ProcessAdmin;

/// <summary>
/// PR-K5 §8.4 — <c>GET /api/admin/process-admin/reports/per-spec</c>.
/// Routing + period parsing + bad-request handling. The aggregator's
/// math is covered by ProcessReportingServiceTests.
/// </summary>
public sealed class ReportsEndpointTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public ReportsEndpointTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn).AddInterceptors(interceptor).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task PerSpec_returnsShape_withZeroes_forUnknownSpec()
    {
        var stub = new StubReportingService(new PerSpecReport(
            SpecCode: "LEAVE", Period: ReportPeriod.Last30Days,
            TotalStarted: 0, TotalCompleted: 0, TotalCancelled: 0, TotalRunning: 0,
            AvgCycleTimeHours: 0, P50CycleTimeHours: 0, P90CycleTimeHours: 0,
            BreachCount: 0, BreachRate: 0,
            Bottlenecks: Array.Empty<NodeBottleneck>(),
            AssigneeLoads: Array.Empty<AssigneeLoad>(),
            CycleTimeHistogram: Array.Empty<CycleTimeBucket>()));
        var action = await BuildController(stub).PerSpecReport("LEAVE", "30d", default);
        var ok = Assert.IsType<OkObjectResult>(action);
        var report = Assert.IsType<PerSpecReport>(ok.Value);
        Assert.Equal("LEAVE", report.SpecCode);
        Assert.Equal(ReportPeriod.Last30Days, stub.LastPeriod);
    }

    [Theory]
    [InlineData("7d",  ReportPeriod.Last7Days)]
    [InlineData("30d", ReportPeriod.Last30Days)]
    [InlineData("90d", ReportPeriod.Last90Days)]
    [InlineData("all", ReportPeriod.AllTime)]
    public async Task PerSpec_parsesPeriodToken(string token, ReportPeriod expected)
    {
        var stub = new StubReportingService(NullReport("LEAVE", expected));
        var action = await BuildController(stub).PerSpecReport("LEAVE", token, default);
        Assert.IsType<OkObjectResult>(action);
        Assert.Equal(expected, stub.LastPeriod);
    }

    [Fact]
    public async Task PerSpec_400_onUnknownPeriod()
    {
        var stub = new StubReportingService(NullReport("LEAVE", ReportPeriod.Last30Days));
        var action = await BuildController(stub).PerSpecReport("LEAVE", "bogus", default);
        var bad = Assert.IsType<BadRequestObjectResult>(action);
        Assert.NotNull(bad.Value);
    }

    [Fact]
    public async Task PerSpec_400_whenSpecCodeMissing()
    {
        var stub = new StubReportingService(NullReport("LEAVE", ReportPeriod.Last30Days));
        var action = await BuildController(stub).PerSpecReport("   ", "30d", default);
        Assert.IsType<BadRequestObjectResult>(action);
    }

    private static PerSpecReport NullReport(string spec, ReportPeriod period) => new(
        SpecCode: spec, Period: period,
        TotalStarted: 0, TotalCompleted: 0, TotalCancelled: 0, TotalRunning: 0,
        AvgCycleTimeHours: 0, P50CycleTimeHours: 0, P90CycleTimeHours: 0,
        BreachCount: 0, BreachRate: 0,
        Bottlenecks: Array.Empty<NodeBottleneck>(),
        AssigneeLoads: Array.Empty<AssigneeLoad>(),
        CycleTimeHistogram: Array.Empty<CycleTimeBucket>());

    private ProcessAdminController BuildController(IProcessReportingService reporting)
    {
        var db = new AppDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var controller = new ProcessAdminController(
            db, config,
            new ThrowingSimulator(), new ThrowingQueryService(), new ThrowingInterventionService(),
            reporting,
            NullLogger<ProcessAdminController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(AdminId) };
        return controller;
    }

    private static DefaultHttpContext HttpContextFor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("roles", "admin"),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class StubReportingService(PerSpecReport report) : IProcessReportingService
    {
        public ReportPeriod LastPeriod { get; private set; }
        public Task<PerSpecReport> GetPerSpecReportAsync(string specCode, ReportPeriod period, CancellationToken ct = default)
        {
            LastPeriod = period;
            return Task.FromResult(report);
        }
    }

    private sealed class ThrowingSimulator : IProcessSimulator
    {
        public Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
    }

    private sealed class ThrowingQueryService : IProcessQueryService
    {
        public Task<ProcessInstanceDto> GetInstanceAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<HistoryPage> GetHistoryPageAsync(Guid instanceId, Guid requesterUserId, string? cursor, int limit, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<IReadOnlyList<ProcessTaskDto>> GetMineAsync(Guid requesterUserId, string status, int limit, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<TaskWithFormDto> GetTaskAsync(Guid taskId, Guid requesterUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<IReadOnlyList<ActiveCaseDto>> GetActiveCasesAsync(string? specCode = null, int? maxAgeDays = null, bool breachOnly = false, int limit = 100, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<LiveCaseDetailDto> GetCaseDetailAsync(Guid instanceId, int historyLimit = 20, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task<CompletedCasesPage> GetCompletedCasesAsync(string? specCode = null, DateTime? completedAfter = null, string? status = null, int limit = 100, string? cursor = null, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
    }

    private sealed class ThrowingInterventionService : IProcessAdminInterventionService
    {
        public Task ForceReassignAsync(Guid taskId, Guid newAssigneeUserId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task ForceReturnAsync(Guid taskId, string targetNodeId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task ForceSubmitAsync(Guid taskId, JsonElement? formDataPatch, Decision? decision, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
        public Task TerminateAsync(Guid instanceId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
    }
}

using System.Security.Claims;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Application.Process.Admin;
using Bpm.Application.Process.Reporting;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Process.Simulator;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Process;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace Bpm.Tests.Api.Admin.ProcessAdmin;

/// <summary>
/// PR-K5 §7.1 — <c>GET /api/admin/process-admin/cases/completed</c>.
/// Drives a real <see cref="ProcessQueryService"/> against an in-memory
/// SQLite seeded with terminal instances. The controller is a thin
/// pass-through, so cursor pagination + filter shaping live in the service
/// — these tests exercise the integration end-to-end through the HTTP
/// surface.
/// </summary>
public sealed class CompletedCasesEndpointTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private readonly Guid _initiatorId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public CompletedCasesEndpointTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn).AddInterceptors(interceptor).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Users.Add(new User
        {
            Id = _initiatorId, DisplayName = "wilson", Email = "wilson@e2e", FullName = "Wilson",
        });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task EmptyDb_returnsEmptyPage()
    {
        var page = await BuildController().ListCompletedCases(null, null, null, 0, null, default);
        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Pagination_returnsCursor_andSecondCallContinues()
    {
        // Seed 3 completed instances at distinct timestamps.
        for (var i = 0; i < 3; i++)
            await SeedCompletedAsync("LEAVE", _clock.UtcNow.AddHours(-i - 1), _clock.UtcNow.AddMinutes(-i));

        var first = await BuildController().ListCompletedCases(null, null, null, limit: 2, null, default);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await BuildController().ListCompletedCases(null, null, null, limit: 2, first.NextCursor, default);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);

        // No overlap.
        var ids1 = first.Items.Select(i => i.Id).ToHashSet();
        Assert.DoesNotContain(second.Items[0].Id, ids1);
    }

    [Fact]
    public async Task FilterBySpecCode_excludesOthers()
    {
        await SeedCompletedAsync("LEAVE",    _clock.UtcNow.AddHours(-1), _clock.UtcNow);
        await SeedCompletedAsync("PURCHASE", _clock.UtcNow.AddHours(-1), _clock.UtcNow);

        var page = await BuildController().ListCompletedCases("LEAVE", null, null, 0, null, default);
        Assert.Single(page.Items);
        Assert.Equal("LEAVE", page.Items[0].SpecCode);
    }

    [Fact]
    public async Task FilterByStatus_completedOnly_excludesCancelled()
    {
        await SeedCompletedAsync("LEAVE", _clock.UtcNow.AddHours(-1), _clock.UtcNow);
        await SeedCancelledAsync("LEAVE", _clock.UtcNow.AddHours(-2), _clock.UtcNow);

        var page = await BuildController().ListCompletedCases(null, null, "completed", 0, null, default);
        Assert.Single(page.Items);
        Assert.Equal(InstanceStatus.Completed, page.Items[0].Status);
    }

    [Fact]
    public async Task CycleTime_isTerminalMinusStarted()
    {
        var started = _clock.UtcNow.AddHours(-3);
        var completed = _clock.UtcNow;
        await SeedCompletedAsync("LEAVE", started, completed);

        var page = await BuildController().ListCompletedCases(null, null, null, 0, null, default);
        var only = Assert.Single(page.Items);
        Assert.NotNull(only.CycleTime);
        Assert.Equal(TimeSpan.FromHours(3), only.CycleTime!.Value);
        Assert.Equal("wilson@e2e", only.InitiatorEmail);
    }

    // ===== harness =====

    private ProcessAdminController BuildController()
    {
        var db = new AppDbContext(_options);
        var query = new ProcessQueryService(new AppDbContext(_options), _clock);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var controller = new ProcessAdminController(
            db, config,
            new ThrowingSimulator(), query, new ThrowingInterventionService(),
            new ThrowingReportingService(),
            NullLogger<ProcessAdminController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(AdminId) };
        return controller;
    }

    private async Task SeedCompletedAsync(string specCode, DateTime startedAt, DateTime completedAt)
    {
        await using var db = new AppDbContext(_options);
        db.ProcessInstances.Add(new ProcessInstance
        {
            Id = Guid.NewGuid(),
            SpecCode = specCode,
            SpecVersion = 1,
            InitiatorUserId = _initiatorId,
            Status = InstanceStatus.Completed,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            LastActivityAt = completedAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedCancelledAsync(string specCode, DateTime startedAt, DateTime cancelledAt)
    {
        await using var db = new AppDbContext(_options);
        db.ProcessInstances.Add(new ProcessInstance
        {
            Id = Guid.NewGuid(),
            SpecCode = specCode,
            SpecVersion = 1,
            InitiatorUserId = _initiatorId,
            Status = InstanceStatus.Cancelled,
            StartedAt = startedAt,
            CancelledAt = cancelledAt,
            CancelReason = "test cancel",
            LastActivityAt = cancelledAt,
        });
        await db.SaveChangesAsync();
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

    private sealed class ThrowingSimulator : IProcessSimulator
    {
        public Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
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

    private sealed class ThrowingReportingService : IProcessReportingService
    {
        public Task<PerSpecReport> GetPerSpecReportAsync(string specCode, ReportPeriod period, CancellationToken ct = default)
            => throw new InvalidOperationException("not used");
    }
}

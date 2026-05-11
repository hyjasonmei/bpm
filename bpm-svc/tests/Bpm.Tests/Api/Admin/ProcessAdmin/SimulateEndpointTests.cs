using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Application.Process.Admin;
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
/// PR-K3 §4.3 — <c>POST /api/admin/process-admin/simulate</c>. The
/// endpoint is a thin pass-through to <see cref="IProcessSimulator"/>;
/// these tests focus on routing and request-shape contract, using a stub
/// simulator so we don't need to spin up the full runtime stack (that
/// path is covered by <c>ProcessSimulatorTests</c>).
/// </summary>
public sealed class SimulateEndpointTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public SimulateEndpointTests()
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
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Simulate_returns_simulator_result_verbatim()
    {
        // Stub returns a hand-built happy-path result; the controller must
        // pass it through unchanged.
        using var formDoc = JsonDocument.Parse("{\"days\":3}");
        using var finalDoc = JsonDocument.Parse("{\"days\":3,\"approved\":true}");
        var stub = new StubSimulator(req => new SimulationResult(
            Success: true,
            Error: null,
            Trace: new[]
            {
                new SimulationStep("start_1", "startEvent", "auto-advanced", null, null, null, null),
                new SimulationStep("task_apply", "userTask", "completed",
                    Guid.NewGuid(), "Bob", null, null),
            },
            Notifications: new[]
            {
                new SimulationNotification("notify_1", "on_assign", "[BPM] please review", new[] { "current_approver" }),
            },
            FinalFormData: finalDoc.RootElement.Clone(),
            FinalStatus: "Completed"));

        var result = await BuildController(stub).Simulate(
            new SimulateRequest("LEAVE", formDoc.RootElement.Clone(), null, null), default);

        Assert.True(result.Success);
        Assert.Equal("Completed", result.FinalStatus);
        Assert.Equal(2, result.Trace.Count);
        Assert.Single(result.Notifications);
    }

    [Fact]
    public async Task Simulate_returns_failure_payload_for_unknown_flow_without_throwing()
    {
        // A simulation failure (unknown flow, runtime error) is NOT an HTTP
        // error per the API contract — Success=false in the body.
        var stub = new StubSimulator(req => new SimulationResult(
            Success: false,
            Error: $"flow '{req.FlowCode}' not found",
            Trace: Array.Empty<SimulationStep>(),
            Notifications: Array.Empty<SimulationNotification>(),
            FinalFormData: null,
            FinalStatus: null));

        using var formDoc = JsonDocument.Parse("{}");
        var result = await BuildController(stub).Simulate(
            new SimulateRequest("DOES_NOT_EXIST", formDoc.RootElement.Clone(), null, null), default);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("DOES_NOT_EXIST", result.Error!);
    }

    [Fact]
    public async Task Simulate_forwards_initiator_override_to_simulator()
    {
        var initiatorId = Guid.NewGuid();
        SimulationRequest? captured = null;
        var stub = new StubSimulator(req =>
        {
            captured = req;
            return EmptyResult();
        });

        using var formDoc = JsonDocument.Parse("{}");
        await BuildController(stub).Simulate(
            new SimulateRequest("LEAVE", formDoc.RootElement.Clone(), null, initiatorId), default);

        Assert.NotNull(captured);
        Assert.Equal(initiatorId, captured!.InitiatorUserId);
        Assert.Equal("LEAVE", captured.FlowCode);
    }

    [Fact]
    public async Task Simulate_forwards_form_data_payload()
    {
        SimulationRequest? captured = null;
        var stub = new StubSimulator(req =>
        {
            captured = req;
            return EmptyResult();
        });

        using var formDoc = JsonDocument.Parse("{\"leave_type\":\"病假\",\"days\":5}");
        await BuildController(stub).Simulate(
            new SimulateRequest("LEAVE", formDoc.RootElement.Clone(), null, null), default);

        Assert.NotNull(captured);
        Assert.Equal("病假", captured!.FormData.GetProperty("leave_type").GetString());
        Assert.Equal(5, captured.FormData.GetProperty("days").GetInt32());
    }

    /* ────────────────────────────────────────────────────────────── */

    private static SimulationResult EmptyResult() => new(
        Success: true, Error: null,
        Trace: Array.Empty<SimulationStep>(),
        Notifications: Array.Empty<SimulationNotification>(),
        FinalFormData: null, FinalStatus: "Completed");

    private ProcessAdminController BuildController(IProcessSimulator sim)
    {
        var db = new AppDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var controller = new ProcessAdminController(db, config, sim,
            new ThrowingQueryService(), new ThrowingInterventionService(),
            NullLogger<ProcessAdminController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = HttpContextFor(Guid.NewGuid()),
        };
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

    private sealed class StubSimulator(Func<SimulationRequest, SimulationResult> impl) : IProcessSimulator
    {
        public Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
            => Task.FromResult(impl(req));
    }

    /// <summary>
    /// PR-K4: SimulateEndpointTests don't exercise the LiveCases / intervention
    /// endpoints; injecting throw-on-call stubs means a regression that
    /// accidentally hits these from the simulator path is loud, not silent.
    /// </summary>
    private sealed class ThrowingQueryService : IProcessQueryService
    {
        public Task<ProcessInstanceDto> GetInstanceAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task<HistoryPage> GetHistoryPageAsync(Guid instanceId, Guid requesterUserId, string? cursor, int limit, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task<IReadOnlyList<ProcessTaskDto>> GetMineAsync(Guid requesterUserId, string status, int limit, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task<TaskWithFormDto> GetTaskAsync(Guid taskId, Guid requesterUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task<IReadOnlyList<ActiveCaseDto>> GetActiveCasesAsync(string? specCode = null, int? maxAgeDays = null, bool breachOnly = false, int limit = 100, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task<LiveCaseDetailDto> GetCaseDetailAsync(Guid instanceId, int historyLimit = 20, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
    }

    private sealed class ThrowingInterventionService : IProcessAdminInterventionService
    {
        public Task ForceReassignAsync(Guid taskId, Guid newAssigneeUserId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task ForceReturnAsync(Guid taskId, string targetNodeId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task ForceSubmitAsync(Guid taskId, JsonElement? formDataPatch, Decision? decision, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
        public Task TerminateAsync(Guid instanceId, string reason, Guid actorUserId, CancellationToken ct = default)
            => throw new InvalidOperationException("not configured for this test");
    }
}

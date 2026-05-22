using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Admin;
using Bpm.Application.Process.Reporting;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Application.Process.Simulator;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Process;
using Bpm.Persistence.Process.Admin;
using Bpm.Tests.Persistence.Process;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Api.Admin.ProcessAdmin;

/// <summary>
/// PR-K4 §6 — controller-layer happy/sad paths for the four intervention
/// endpoints. We instantiate <see cref="ProcessAdminController"/> directly
/// (matching the existing K1/K2/K3 test style) and route through real
/// service implementations so the exception → HTTP-status mapping is
/// covered end-to-end. Authorization is asserted at the attribute level
/// rather than via the runtime middleware since these tests don't spin up
/// the full <c>WebApplicationFactory</c>; a separate metadata assertion
/// confirms <c>[Authorize(Roles="admin")]</c> still gates the controller.
/// </summary>
public sealed class InterventionEndpointTests : IDisposable
{
    private readonly ProcessRuntimeE2EFixture _e2e;
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public InterventionEndpointTests()
    {
        _e2e = new ProcessRuntimeE2EFixture();
    }

    public void Dispose() => _e2e.Dispose();

    [Fact]
    public async Task ForceReassign_returns_200_and_persists()
    {
        var (instanceId, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();

        var result = await controller.ForceReassign(taskId,
            new ForceReassignRequest(NewAssigneeUserId: _e2e.LinId, Reason: "manager OOO"), default);

        Assert.IsType<OkObjectResult>(result);
        using var read = new AppDbContext(_e2e.Options);
        var task = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        Assert.Equal(_e2e.LinId, task.ActualAssigneeUserId);
    }

    [Fact]
    public async Task ForceReassign_empty_reason_throws_validation()
    {
        var (_, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();
        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.ForceReassign(taskId,
                new ForceReassignRequest(_e2e.LinId, Reason: " "), default));
    }

    [Fact]
    public async Task ForceReassign_unknown_task_throws_not_found()
    {
        await SetupOpenManagerTaskAsync(); // ensure DB exists, even though we use a random id
        var controller = BuildController();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            controller.ForceReassign(Guid.NewGuid(),
                new ForceReassignRequest(_e2e.LinId, "valid"), default));
    }

    [Fact]
    public async Task ForceReturn_returns_200_and_spawns_target_task()
    {
        var (instanceId, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();

        var result = await controller.ForceReturn(taskId,
            new ForceReturnRequest(TargetNodeId: "task_apply", Reason: "needs revision"), default);

        Assert.IsType<OkObjectResult>(result);
        using var read = new AppDbContext(_e2e.Options);
        var spawned = await read.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == instanceId
                        && t.NodeId == "task_apply" && t.Status == TaskStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(spawned);
    }

    [Fact]
    public async Task ForceReturn_empty_reason_throws_validation()
    {
        var (_, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();
        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.ForceReturn(taskId, new ForceReturnRequest("task_apply", ""), default));
    }

    [Fact]
    public async Task ForceSubmit_returns_200_and_advances()
    {
        var (instanceId, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();

        var result = await controller.ForceSubmit(taskId,
            new ForceSubmitRequest(FormDataPatch: null, Decision: Decision.Approve, Reason: "manager away"), default);

        Assert.IsType<OkObjectResult>(result);
        using var read = new AppDbContext(_e2e.Options);
        var done = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        Assert.Equal(TaskStatus.Completed, done.Status);
    }

    [Fact]
    public async Task ForceSubmit_empty_reason_throws_validation()
    {
        var (_, taskId) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();
        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.ForceSubmit(taskId,
                new ForceSubmitRequest(null, Decision.Approve, Reason: ""), default));
    }

    [Fact]
    public async Task Terminate_returns_200_and_marks_instance_cancelled()
    {
        var (instanceId, _) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();

        var result = await controller.Terminate(instanceId,
            new TerminateRequest(Reason: "duplicate"), default);

        Assert.IsType<OkObjectResult>(result);
        using var read = new AppDbContext(_e2e.Options);
        var instance = await read.ProcessInstances.AsNoTracking().FirstAsync(i => i.Id == instanceId);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
    }

    [Fact]
    public async Task Terminate_empty_reason_throws_validation()
    {
        var (instanceId, _) = await SetupOpenManagerTaskAsync();
        var controller = BuildController();
        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Terminate(instanceId, new TerminateRequest(""), default));
    }

    [Fact]
    public async Task Terminate_unknown_instance_throws_not_found()
    {
        await SetupOpenManagerTaskAsync();
        var controller = BuildController();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            controller.Terminate(Guid.NewGuid(), new TerminateRequest("x"), default));
    }

    [Fact]
    public void Controller_class_requires_admin_role()
    {
        // Cheap, deterministic substitute for "non-admin → 403" — the
        // [Authorize(Roles="admin")] attribute is the production gate. A
        // full WebApplicationFactory roundtrip would re-test ASP.NET's
        // own authorization filter; the metadata check confirms the
        // contract our security review actually cares about.
        var attr = typeof(ProcessAdminController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .FirstOrDefault();
        Assert.NotNull(attr);
        Assert.Equal("admin", attr!.Roles);
    }

    /* ────────────────────────────────────────────────────────────── */

    /// <summary>
    /// Walk the LEAVE spec to the manager-approval task and return both
    /// the instance id (for terminate / detail lookups) and the open task
    /// id (for the per-task force endpoints).
    /// </summary>
    private async Task<(Guid instanceId, Guid taskId)> SetupOpenManagerTaskAsync()
    {
        var (db, runtime, _) = _e2e.Build();
        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _e2e.WilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _e2e.WilsonId, null, null, null));

        var managerTask = await db.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && t.NodeId == "approval_manager"
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();

        db.Dispose();
        return (start.InstanceId, managerTask.Id);
    }

    private ProcessAdminController BuildController()
    {
        // Build a fresh stack per call (mirrors how ProcessQueryService /
        // intervention service would resolve out of DI in production).
        var db = new AppDbContext(_e2e.Options);
        var query = new ProcessQueryService(db, _e2e.Clock);
        var (_, runtime, _) = _e2e.Build();
        var interventionDb = new AppDbContext(_e2e.Options);
        var intervention = new ProcessAdminInterventionService(interventionDb, _e2e.Clock, runtime);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var controller = new ProcessAdminController(
            db, config, new ThrowingSimulator(),
            query, intervention,
            new ThrowingReportingService(),
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

    private sealed class ThrowingSimulator : IProcessSimulator
    {
        public Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
            => throw new InvalidOperationException("not used in intervention tests");
    }

    private sealed class ThrowingReportingService : IProcessReportingService
    {
        public Task<PerSpecReport> GetPerSpecReportAsync(string specCode, ReportPeriod period, CancellationToken ct = default)
            => throw new InvalidOperationException("not used in intervention tests");
    }
}

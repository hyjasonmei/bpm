using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Process;
using Bpm.Persistence.Process.Admin;
using Bpm.Tests.Common;
using Bpm.Tests.Persistence.Process;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Persistence.Process.Admin;

/// <summary>
/// PR-K4 §6.1 — admin intervention service. Each test drives a real
/// ProcessRuntime against the LEAVE sample spec to spawn at least one open
/// task, then exercises a force-action and asserts both the resulting state
/// shape and the audit-trail row(s) carry the <c>actorRole = "admin"</c>
/// marker plus the operation-specific payload fields.
/// </summary>
public sealed class ProcessAdminInterventionTests : IDisposable
{
    private readonly ProcessRuntimeE2EFixture _e2e;
    private readonly Guid _adminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public ProcessAdminInterventionTests()
    {
        _e2e = new ProcessRuntimeE2EFixture();
    }

    public void Dispose() => _e2e.Dispose();

    [Fact]
    public async Task ForceReassign_swaps_assignee_and_writes_admin_history()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var newAssignee = ctx.Lin;

        var svc = ctx.BuildIntervention();
        await svc.ForceReassignAsync(ctx.Task.Id, newAssignee, "off-sick", _adminId, default);

        using var read = new AppDbContext(ctx.Options);
        var task = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == ctx.Task.Id);
        Assert.Equal(newAssignee, task.ActualAssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.ClaimedAt);

        var hist = await read.TaskHistory.AsNoTracking()
            .Where(h => h.TaskId == ctx.Task.Id && h.EventType == HistoryEventType.TaskClaimed && h.ActorUserId == _adminId)
            .FirstOrDefaultAsync();
        Assert.NotNull(hist);
        using var doc = JsonDocument.Parse(hist!.PayloadJson);
        Assert.Equal("admin", doc.RootElement.GetProperty("actorRole").GetString());
        Assert.Equal(newAssignee, doc.RootElement.GetProperty("newAssigneeUserId").GetGuid());
        Assert.Equal("off-sick", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ForceReassign_throws_when_task_already_completed()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        // Submit (approve) the task so it lands in Completed.
        await ctx.Runtime.SubmitTaskAsync(new SubmitTaskCommand(ctx.Task.Id, ctx.Yang, null, Decision.Approve, "ok"));

        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ForceReassignAsync(ctx.Task.Id, ctx.Lin, "any", _adminId, default));
    }

    [Fact]
    public async Task ForceReassign_empty_reason_throws_validation()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ForceReassignAsync(ctx.Task.Id, ctx.Lin, "   ", _adminId, default));
    }

    [Fact]
    public async Task ForceReturn_cancels_current_and_spawns_at_target_node()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        const string targetNode = "task_apply"; // initial userTask in LEAVE spec

        var svc = ctx.BuildIntervention();
        await svc.ForceReturnAsync(ctx.Task.Id, targetNode, "missing receipt", _adminId, default);

        using var read = new AppDbContext(ctx.Options);
        var original = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == ctx.Task.Id);
        Assert.Equal(TaskStatus.Cancelled, original.Status);
        Assert.Equal(Decision.Return, original.Decision);

        var spawned = await read.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == ctx.InstanceId
                        && t.NodeId == targetNode
                        && t.Status == TaskStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();
        Assert.Equal(_adminId, spawned.ActualAssigneeUserId);

        var returnHist = await read.TaskHistory.AsNoTracking()
            .Where(h => h.TaskId == ctx.Task.Id && h.EventType == HistoryEventType.TaskReturned)
            .FirstAsync();
        using var doc = JsonDocument.Parse(returnHist.PayloadJson);
        Assert.Equal("admin", doc.RootElement.GetProperty("actorRole").GetString());
        Assert.Equal(targetNode, doc.RootElement.GetProperty("toNodeId").GetString());
        Assert.Equal("missing receipt", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ForceReturn_unknown_node_throws_conflict()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.ForceReturnAsync(ctx.Task.Id, "node_does_not_exist", "fix it", _adminId, default));
    }

    [Fact]
    public async Task ForceSubmit_bypasses_actor_check_and_advances_flow()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        // Admin (NOT the manager) approves on Yang's behalf.
        var svc = ctx.BuildIntervention();
        await svc.ForceSubmitAsync(ctx.Task.Id, formDataPatch: null, decision: Decision.Approve,
            reason: "manager unavailable, days < 7 → straight to HR", actorUserId: _adminId, ct: default);

        using var read = new AppDbContext(ctx.Options);
        var completed = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == ctx.Task.Id);
        Assert.Equal(TaskStatus.Completed, completed.Status);
        Assert.Equal(Decision.Approve, completed.Decision);

        // Next task should be HR archive (3-day leave routes <7 → HR).
        var hr = await read.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == ctx.InstanceId
                        && t.NodeId == "task_hr_archive"
                        && t.Status == TaskStatus.Pending)
            .FirstOrDefaultAsync();
        Assert.NotNull(hr);

        var marker = await read.TaskHistory.AsNoTracking()
            .Where(h => h.TaskId == ctx.Task.Id
                        && h.EventType == HistoryEventType.TaskSubmitted
                        && h.ActorUserId == _adminId)
            .FirstAsync();
        using var doc = JsonDocument.Parse(marker.PayloadJson);
        Assert.Equal("admin", doc.RootElement.GetProperty("actorRole").GetString());
        Assert.Equal("Approve", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task ForceSubmit_empty_reason_throws()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.ForceSubmitAsync(ctx.Task.Id, null, Decision.Approve, "", _adminId, default));
    }

    [Fact]
    public async Task Terminate_cancels_instance_and_open_tasks_with_admin_marker()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();

        var svc = ctx.BuildIntervention();
        await svc.TerminateAsync(ctx.InstanceId, "duplicate request", _adminId, default);

        using var read = new AppDbContext(ctx.Options);
        var instance = await read.ProcessInstances.AsNoTracking().FirstAsync(i => i.Id == ctx.InstanceId);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
        Assert.Equal("duplicate request", instance.CancelReason);

        var openTasks = await read.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == ctx.InstanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync();
        Assert.Empty(openTasks);

        var adminMarker = await read.TaskHistory.AsNoTracking()
            .Where(h => h.ProcessInstanceId == ctx.InstanceId
                        && h.EventType == HistoryEventType.InstanceCancelled
                        && h.ActorUserId == _adminId)
            .FirstAsync();
        using var doc = JsonDocument.Parse(adminMarker.PayloadJson);
        Assert.Equal("admin", doc.RootElement.GetProperty("actorRole").GetString());
        Assert.Equal("duplicate request", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Terminate_empty_reason_throws()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.TerminateAsync(ctx.InstanceId, "  ", _adminId, default));
    }

    [Fact]
    public async Task Terminate_unknown_instance_throws_404_equivalent()
    {
        var ctx = await StartLeaveAndGetManagerTaskAsync();
        var svc = ctx.BuildIntervention();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            svc.TerminateAsync(Guid.NewGuid(), "x", _adminId, default));
    }

    /* ────────────────────────────────────────────────────────────── */

    /// <summary>
    /// Spin up the LEAVE flow far enough that the manager-approval task is
    /// open and assigned to Yang. We DON'T dispose the runtime's
    /// AppDbContext here — the BuildIntervention() helper builds a fresh
    /// ProcessRuntime per-test, so each force-action gets a clean DbContext
    /// and previous setup work is committed durably to the shared
    /// in-memory SQLite (the connection lifetime is owned by the fixture).
    /// </summary>
    private async Task<TestContext> StartLeaveAndGetManagerTaskAsync()
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

        db.Dispose(); // free the setup-time DbContext; Build() returns a fresh one per intervention call

        return new TestContext(
            Fixture: _e2e,
            InstanceId: start.InstanceId,
            Task: managerTask,
            Yang: _e2e.YangId,
            Lin: _e2e.LinId)
        {
            Options = _e2e.Options,
            Clock = _e2e.Clock,
        };
    }

    private sealed record TestContext(
        ProcessRuntimeE2EFixture Fixture,
        Guid InstanceId,
        ProcessTask Task,
        Guid Yang,
        Guid Lin)
    {
        public DbContextOptions<AppDbContext> Options { get; init; } = null!;
        public IClock Clock { get; init; } = null!;

        // PR-K4: Each force-action call gets its own ProcessRuntime + DbContext
        // so the runtime's per-call EF transaction is scoped to a live context.
        // Re-using a single Runtime across calls hits ObjectDisposedException
        // because EF's per-DbContext lifetime tracking sees the previous tx
        // boundary closed by SaveChangesAsync.
        public IProcessRuntime Runtime
        {
            get
            {
                var (_, runtime, _) = Fixture.Build();
                return runtime;
            }
        }

        public ProcessAdminInterventionService BuildIntervention()
        {
            var (db, runtime, _) = Fixture.Build();
            return new ProcessAdminInterventionService(db, Clock, runtime);
        }
    }
}

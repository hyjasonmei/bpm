using System.Text.Json;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Api.Process;

public sealed class TaskControllerTests
{
    [Fact]
    public async Task Mine_returns_assigned_open_tasks()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        var mine = await asWilson.Mine(status: "open", limit: 50, default);

        Assert.Single(mine);
        Assert.Equal(start.FirstTaskId, mine[0].Id);
        Assert.Equal(TaskStatus.Pending, mine[0].Status);
    }

    [Fact]
    public async Task Mine_for_unassigned_user_is_empty()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asAmy = fix.BuildTaskController(runtime, query, fix.AmyId);
        var mine = await asAmy.Mine(status: "open", limit: 50, default);
        Assert.Empty(mine);
    }

    [Fact]
    public async Task Get_task_returns_form_snapshot()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        var dto = await asWilson.GetById(start.FirstTaskId, default);

        Assert.Equal(start.FirstTaskId, dto.Task.Id);
        Assert.Equal(start.InstanceId, dto.InstanceId);
        Assert.Equal("LEAVE", dto.SpecCode);
        Assert.Equal("特休", dto.MergedFormData.GetProperty("leave_type").GetString());
    }

    [Fact]
    public async Task Get_task_by_wrong_user_throws_forbidden()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asAmy = fix.BuildTaskController(runtime, query, fix.AmyId);
        await Assert.ThrowsAsync<ForbiddenException>(() => asAmy.GetById(start.FirstTaskId, default));
    }

    [Fact]
    public async Task Submit_with_approve_advances_to_next_node()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        // Wilson submits the apply userTask with days=5.
        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        var patch = JsonDocument.Parse("""{"days":5}""").RootElement;
        var submit = await asWilson.Submit(start.FirstTaskId,
            new SubmitTaskRequest(patch, Decision: null, Comment: "applying"), default);
        Assert.IsType<NoContentResult>(submit);

        // Manager approval task should now exist for Elton; Elton approves it.
        using var read = new AppDbContext(fix.Options);
        var managerTask = await read.ProcessTasks
            .FirstAsync(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_manager");

        var asElton = fix.BuildTaskController(runtime, query, fix.EltonId);
        var approve = await asElton.Submit(managerTask.Id,
            new SubmitTaskRequest(default, Decision: "Approve", Comment: "ok"), default);
        Assert.IsType<NoContentResult>(approve);

        using var read2 = new AppDbContext(fix.Options);
        var fresh = await read2.ProcessTasks.FirstAsync(t => t.Id == managerTask.Id);
        Assert.Equal(TaskStatus.Completed, fresh.Status);
        Assert.Equal(Decision.Approve, fresh.Decision);
    }

    [Fact]
    public async Task Submit_with_invalid_decision_throws_conflict()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        await Assert.ThrowsAsync<ConflictException>(() =>
            asWilson.Submit(start.FirstTaskId,
                new SubmitTaskRequest(default, Decision: "Maybe", Comment: null), default));
    }

    [Fact]
    public async Task Submit_by_wrong_user_throws_forbidden()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asAmy = fix.BuildTaskController(runtime, query, fix.AmyId);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            asAmy.Submit(start.FirstTaskId,
                new SubmitTaskRequest(default, Decision: null, Comment: "nope"), default));
    }

    [Fact]
    public async Task Return_requires_non_empty_comment()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        await Assert.ThrowsAsync<ConflictException>(() =>
            asWilson.Return(start.FirstTaskId, new ReturnTaskRequest(""), default));

        await Assert.ThrowsAsync<ConflictException>(() =>
            asWilson.Return(start.FirstTaskId, null, default));
    }

    [Fact]
    public async Task Return_advances_back_to_previous_userTask()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        // Wilson submits → manager approval task exists.
        var asWilson = fix.BuildTaskController(runtime, query, fix.WilsonId);
        var patch = JsonDocument.Parse("""{"days":5}""").RootElement;
        await asWilson.Submit(start.FirstTaskId,
            new SubmitTaskRequest(patch, Decision: null, Comment: null), default);

        using var read = new AppDbContext(fix.Options);
        var managerTask = await read.ProcessTasks
            .FirstAsync(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_manager");

        var asElton = fix.BuildTaskController(runtime, query, fix.EltonId);
        var ret = await asElton.Return(managerTask.Id, new ReturnTaskRequest("please clarify"), default);
        Assert.IsType<NoContentResult>(ret);

        using var read2 = new AppDbContext(fix.Options);
        var bouncedManager = await read2.ProcessTasks.FirstAsync(t => t.Id == managerTask.Id);
        Assert.Equal(TaskStatus.Completed, bouncedManager.Status);
        Assert.Equal(Decision.Return, bouncedManager.Decision);

        // A new apply task should exist back at task_apply for Wilson.
        var newApply = await read2.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && t.NodeId == "task_apply"
                        && t.Status == TaskStatus.Pending)
            .FirstOrDefaultAsync();
        Assert.NotNull(newApply);
        Assert.Equal(fix.WilsonId, newApply!.ActualAssigneeUserId);
    }

    [Fact]
    public async Task Claim_atomic_when_concurrent_via_controllers()
    {
        // Seed a sibling-pool approval directly so we can assert claim semantics
        // through the controller surface (mirrors ProcessRuntimeTests but at
        // the API layer).
        using var fix = new ProcessTestFixture();
        var (dbA, runtimeA, queryA) = fix.BuildStack();
        var (dbB, runtimeB, queryB) = fix.BuildStack();
        using var _a = dbA;
        using var _b = dbB;

        var inst = new ProcessInstance
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            SpecCode = "LEAVE",
            SpecVersion = 1,
            SpecSnapshotJson = "{}",
            InitiatorUserId = fix.WilsonId,
            Status = InstanceStatus.Running,
            CurrentFormDataJson = "{}",
            StartedAt = fix.Clock.UtcNow,
            LastActivityAt = fix.Clock.UtcNow,
        };
        var poolTask = new ProcessTask
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = inst.Id,
            NodeId = "approval_pool",
            NodeKind = NodeKind.Approval,
            OriginalAssigneeUserId = fix.EltonId,
            ActualAssigneeUserId = fix.EltonId,
            Status = TaskStatus.Pending,
        };
        dbA.ProcessInstances.Add(inst);
        dbA.ProcessTasks.Add(poolTask);
        await dbA.SaveChangesAsync();

        var ctrlA = fix.BuildTaskController(runtimeA, queryA, fix.EltonId);
        var ctrlB = fix.BuildTaskController(runtimeB, queryB, fix.EltonId);

        var taskA = ctrlA.Claim(poolTask.Id, default);
        var taskB = ctrlB.Claim(poolTask.Id, default);
        var results = await System.Threading.Tasks.Task.WhenAll(SafeRun(taskA), SafeRun(taskB));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, results.Count(r => r is ConflictException));

        using var read = new AppDbContext(fix.Options);
        var fresh = await read.ProcessTasks.FirstAsync(x => x.Id == poolTask.Id);
        Assert.Equal(TaskStatus.InProgress, fresh.Status);
    }

    private static async Task<Exception?> SafeRun(Task<IActionResult> work)
    {
        try { await work; return null; }
        catch (Exception ex) { return ex; }
    }
}

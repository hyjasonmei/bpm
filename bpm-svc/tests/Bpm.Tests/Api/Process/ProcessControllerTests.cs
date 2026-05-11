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

public sealed class ProcessControllerTests
{
    [Fact]
    public async Task Start_returns_201_with_instance_and_first_task()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var result = await controller.Start(new StartProcessRequest("LEAVE", form), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(Bpm.Api.Process.ProcessController.GetById), created.ActionName);
        // Anonymous-typed body — pull instanceId / firstTaskId via reflection.
        var body = created.Value!;
        var instanceId = (Guid)body.GetType().GetProperty("instanceId")!.GetValue(body)!;
        var firstTaskId = (Guid)body.GetType().GetProperty("firstTaskId")!.GetValue(body)!;
        Assert.NotEqual(Guid.Empty, instanceId);
        Assert.NotEqual(Guid.Empty, firstTaskId);

        using var read = new AppDbContext(fix.Options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == instanceId);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal(fix.WilsonId, instance.InitiatorUserId);
    }

    [Fact]
    public async Task Start_with_missing_specCode_returns_400()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var result = await controller.Start(
            new StartProcessRequest("", default), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetById_returns_instance_with_open_tasks()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var dto = await controller.GetById(start.InstanceId, default);

        Assert.Equal(start.InstanceId, dto.Id);
        Assert.Equal("LEAVE", dto.SpecCode);
        Assert.Equal(InstanceStatus.Running, dto.Status);
        Assert.Single(dto.OpenTasks);
        Assert.Equal(start.FirstTaskId, dto.OpenTasks[0].Id);
    }

    [Fact]
    public async Task GetById_for_other_users_instance_throws_forbidden()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;

        // Wilson starts; Elton tries to read.
        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var asElton = fix.BuildProcessController(runtime, query, fix.EltonId);
        await Assert.ThrowsAsync<ForbiddenException>(() => asElton.GetById(start.InstanceId, default));
    }

    [Fact]
    public async Task GetHistory_paginates_and_emits_next_cursor()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        // Start emits at least InstanceStarted + TaskSpawned. Page size 1 → cursor.
        var page1 = await controller.GetHistory(start.InstanceId, cursor: null, limit: 1, default);
        Assert.Single(page1.Items);
        Assert.NotNull(page1.NextCursor);

        var page2 = await controller.GetHistory(start.InstanceId, cursor: page1.NextCursor, limit: 50, default);
        Assert.NotEmpty(page2.Items);
        // The two pages should not overlap on the first item.
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
    }

    [Fact]
    public async Task Cancel_marks_instance_cancelled()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        var result = await controller.Cancel(start.InstanceId, new CancelProcessRequest("duplicate"), default);
        Assert.IsType<NoContentResult>(result);

        using var read = new AppDbContext(fix.Options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
        Assert.Equal("duplicate", instance.CancelReason);
    }

    [Fact]
    public async Task Cancel_without_reason_throws_conflict()
    {
        using var fix = new ProcessTestFixture();
        var (db, runtime, query) = fix.BuildStack();
        using var _ = db;
        var controller = fix.BuildProcessController(runtime, query, fix.WilsonId);

        var form = JsonDocument.Parse("""{"x":1}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, fix.WilsonId));

        await Assert.ThrowsAsync<ConflictException>(() =>
            controller.Cancel(start.InstanceId, new CancelProcessRequest(""), default));
    }
}

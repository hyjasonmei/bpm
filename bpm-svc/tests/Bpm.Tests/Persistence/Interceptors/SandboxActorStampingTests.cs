using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Interceptors;

/// <summary>
/// PR-J4 §6.5: when a sandbox-persona JWT carries
/// <c>sandbox_actor=true</c> + <c>actual_actor_id</c>, the audit interceptor
/// stamps <c>SandboxActualActor</c> on every newly inserted ProcessTask /
/// TaskHistory row. The non-sandbox path leaves the column null (regression
/// guard for prod traffic).
/// </summary>
public sealed class SandboxActorStampingTests : IDisposable
{
    private readonly SqliteConnection _conn;

    public SandboxActorStampingTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
    }

    public void Dispose() => _conn.Dispose();

    private DbContextOptions<AppDbContext> BuildOptions(ISandboxActorContext sandboxActor)
    {
        var interceptor = new AuditSaveChangesInterceptor(
            new StubClock(), new StubCurrentUser(), sandboxActor);
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;
        using var seed = new AppDbContext(opts);
        seed.Database.EnsureCreated();
        return opts;
    }

    [Fact]
    public async Task ProcessTask_insert_with_sandbox_actor_stamps_actual_actor_id()
    {
        var actualActor = Guid.NewGuid();
        var opts = BuildOptions(new StubSandboxActor(actualActor, isSandbox: true));

        Guid taskId;
        await using (var db = new AppDbContext(opts))
        {
            var instance = new ProcessInstance
            {
                TenantCode = "default",
                SpecCode = "X",
                SpecVersion = 1,
                InitiatorUserId = Guid.NewGuid(),
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
            };
            db.ProcessInstances.Add(instance);
            var task = new ProcessTask
            {
                TenantCode = "default",
                ProcessInstanceId = instance.Id,
                NodeId = "n1",
                NodeKind = NodeKind.UserTask,
            };
            db.ProcessTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }

        await using var verify = new AppDbContext(opts);
        var stored = await verify.ProcessTasks.SingleAsync(x => x.Id == taskId);
        Assert.Equal(actualActor, stored.SandboxActualActor);
    }

    [Fact]
    public async Task TaskHistory_insert_with_sandbox_actor_stamps_actual_actor_id()
    {
        var actualActor = Guid.NewGuid();
        var opts = BuildOptions(new StubSandboxActor(actualActor, isSandbox: true));

        Guid hId;
        await using (var db = new AppDbContext(opts))
        {
            // FK on TaskHistory.ProcessInstanceId requires a real parent.
            var inst = new ProcessInstance
            {
                TenantCode = "default",
                SpecCode = "X",
                SpecVersion = 1,
                InitiatorUserId = Guid.NewGuid(),
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
            };
            db.ProcessInstances.Add(inst);
            var h = new TaskHistory
            {
                TenantCode = "default",
                ProcessInstanceId = inst.Id,
                EventType = HistoryEventType.InstanceStarted,
            };
            db.TaskHistory.Add(h);
            await db.SaveChangesAsync();
            hId = h.Id;
        }

        await using var verify = new AppDbContext(opts);
        var stored = await verify.TaskHistory.SingleAsync(x => x.Id == hId);
        Assert.Equal(actualActor, stored.SandboxActualActor);
    }

    [Fact]
    public async Task ProcessTask_insert_without_sandbox_actor_leaves_column_null()
    {
        // Default no-op sandbox actor — production-like request.
        var opts = BuildOptions(new StubSandboxActor(null, isSandbox: false));

        Guid taskId;
        await using (var db = new AppDbContext(opts))
        {
            var inst = new ProcessInstance
            {
                TenantCode = "default",
                SpecCode = "X",
                SpecVersion = 1,
                InitiatorUserId = Guid.NewGuid(),
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
            };
            db.ProcessInstances.Add(inst);
            var task = new ProcessTask
            {
                TenantCode = "default",
                ProcessInstanceId = inst.Id,
                NodeId = "n1",
                NodeKind = NodeKind.UserTask,
            };
            db.ProcessTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }

        await using var verify = new AppDbContext(opts);
        var stored = await verify.ProcessTasks.SingleAsync(x => x.Id == taskId);
        Assert.Null(stored.SandboxActualActor);
    }

    private sealed class StubSandboxActor(Guid? actualActorId, bool isSandbox) : ISandboxActorContext
    {
        public Guid? ActualActorUserId { get; } = actualActorId;
        public bool IsSandboxActor { get; } = isSandbox;
    }
}

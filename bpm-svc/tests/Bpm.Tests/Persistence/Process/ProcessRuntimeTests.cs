using System.Text.Json;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Delegation;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Spec;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Persistence.Process;

public sealed class ProcessRuntimeTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private readonly Guid _wilsonId = Guid.NewGuid();
    private readonly Guid _eltonId = Guid.NewGuid();
    private readonly Guid _amyId = Guid.NewGuid();
    private readonly Guid _ceoId = Guid.NewGuid();

    public ProcessRuntimeTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
        SeedOrg(seed);
    }

    public void Dispose() => _conn.Dispose();

    private void SeedOrg(AppDbContext db)
    {
        var corp = new Department { Id = Guid.NewGuid(), Code = "CORP", Name = "Corporate", DisplayName = "Corporate" };
        var eng = new Department { Id = Guid.NewGuid(), Code = "ENG", Name = "Engineering", DisplayName = "Engineering", ParentId = corp.Id };

        var ceo = new User { Id = _ceoId, DisplayName = "CEO", Email = "ceo@x", FullName = "CEO", DepartmentId = corp.Id };
        var elton = new User { Id = _eltonId, DisplayName = "Elton", Email = "manager@x", FullName = "Elton Yang", DepartmentId = eng.Id, ManagerId = ceo.Id };
        var wilson = new User { Id = _wilsonId, DisplayName = "Wilson", Email = "employee@x", FullName = "Wilson You", DepartmentId = eng.Id, ManagerId = elton.Id };
        var amy = new User { Id = _amyId, DisplayName = "Amy", Email = "hr@x", FullName = "Amy Lin (HR)", DepartmentId = corp.Id, ManagerId = ceo.Id };

        db.Departments.AddRange(corp, eng);
        db.Users.AddRange(ceo, elton, wilson, amy);

        var hrRole = new Role { Id = Guid.NewGuid(), Code = "HR", Name = "Human Resources", Scope = RoleScope.System };
        var vpRole = new Role { Id = Guid.NewGuid(), Code = "VP", Name = "Vice President", Scope = RoleScope.System };
        db.Roles.AddRange(hrRole, vpRole);
        db.SaveChanges();

        // After SaveChanges (ids stable), assign role and dept head.
        corp.HeadUserId = ceo.Id;
        eng.HeadUserId = elton.Id;
        db.RoleAssignments.Add(new RoleAssignment { RoleId = hrRole.Id, PrincipalId = amy.Id });
        db.RoleAssignments.Add(new RoleAssignment { RoleId = vpRole.Id, PrincipalId = ceo.Id });
        db.SaveChanges();
    }

    private (AppDbContext db, ProcessRuntime runtime) BuildRuntime()
    {
        var db = new AppDbContext(_options);
        var orgReader = new OrgChartReader(db);
        var auditor = new NoOpResolutionAuditor();
        var resolver = new Bpm.Application.Spec.ActorResolver(orgReader, auditor, NullLogger<Bpm.Application.Spec.ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var notifier = new LoggingNotificationDispatcher(NullLogger<LoggingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bpm:SampleSpecsDir"] = "/Users/jason/claude/bpm/sample_specs",
            })
            .Build();
        var loader = new FileSystemSpecLoader(cfg, NullLogger<FileSystemSpecLoader>.Instance);

        var runtime = new ProcessRuntime(db, _clock, loader, resolver, delegation, notifier, evaluator,
            NullLogger<ProcessRuntime>.Instance);
        return (db, runtime);
    }

    [Fact]
    public async Task StartInstanceAsync_creates_instance_and_first_task()
    {
        var (db, runtime) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":5,"reason":"vacation"}""").RootElement;
        var cmd = new StartInstanceCommand("LEAVE", form, _wilsonId);

        var result = await runtime.StartInstanceAsync(cmd);

        Assert.NotEqual(Guid.Empty, result.InstanceId);
        Assert.NotEqual(Guid.Empty, result.FirstTaskId);

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == result.InstanceId);
        Assert.Equal(InstanceStatus.Running, instance.Status);
        Assert.Equal("LEAVE", instance.SpecCode);
        Assert.Equal(_wilsonId, instance.InitiatorUserId);

        var firstTask = await read.ProcessTasks.FirstAsync(t => t.Id == result.FirstTaskId);
        Assert.Equal("task_apply", firstTask.NodeId);
        Assert.Equal(NodeKind.UserTask, firstTask.NodeKind);
        Assert.Equal(_wilsonId, firstTask.ActualAssigneeUserId);
        Assert.Equal(TaskStatus.Pending, firstTask.Status);

        var historyRows = await read.TaskHistory.Where(h => h.ProcessInstanceId == result.InstanceId).ToListAsync();
        Assert.Contains(historyRows, h => h.EventType == HistoryEventType.InstanceStarted);
        Assert.Contains(historyRows, h => h.EventType == HistoryEventType.TaskSpawned);
    }

    [Fact]
    public async Task SubmitTask_advances_to_next_node()
    {
        var (db, runtime) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"vacation"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));

        // Wilson submits task_apply with days=5 patch.
        var patch = JsonDocument.Parse("""{"days":5}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, patch, null, "applying"));

        using var read = new AppDbContext(_options);
        var applyTask = await read.ProcessTasks.FirstAsync(t => t.Id == start.FirstTaskId);
        Assert.Equal(TaskStatus.Completed, applyTask.Status);

        // Manager approval task should now exist for Elton.
        var managerTask = await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_manager")
            .FirstAsync();
        Assert.Equal(NodeKind.Approval, managerTask.NodeKind);
        Assert.Equal(_eltonId, managerTask.ActualAssigneeUserId);
        Assert.Equal(TaskStatus.Pending, managerTask.Status);

        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        var data = JsonDocument.Parse(instance.CurrentFormDataJson).RootElement;
        Assert.Equal(5, data.GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task Gateway_evaluates_with_cel()
    {
        // Branch 1: 5-day leave → days < 7 → goes straight to HR archive (Amy).
        var (db1, runtime1) = BuildRuntime();
        using (db1)
        {
            var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
            var start = await runtime1.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
            var patch5 = JsonDocument.Parse("""{"days":5}""").RootElement;
            await runtime1.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, patch5, null, null));

            using var read = new AppDbContext(_options);
            var managerTask = await read.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_manager")
                .FirstAsync();
            await runtime1.SubmitTaskAsync(new SubmitTaskCommand(managerTask.Id, _eltonId, null, Decision.Approve, "ok"));

            using var read2 = new AppDbContext(_options);
            var hrTask = await read2.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "task_hr_archive")
                .FirstOrDefaultAsync();
            Assert.NotNull(hrTask);
            Assert.Equal(_amyId, hrTask!.ActualAssigneeUserId);
            // No VP task should be spawned for short leave.
            var vpTask = await read2.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_vp")
                .FirstOrDefaultAsync();
            Assert.Null(vpTask);

            var gw = await read2.TaskHistory
                .Where(h => h.ProcessInstanceId == start.InstanceId && h.EventType == HistoryEventType.GatewayEvaluated)
                .FirstOrDefaultAsync();
            Assert.NotNull(gw);
        }

        // Branch 2: 8-day leave → days >= 7 → routes to VP approval first.
        var (db2, runtime2) = BuildRuntime();
        using (db2)
        {
            var form = JsonDocument.Parse("""{"leave_type":"事假","reason":"trip"}""").RootElement;
            var start = await runtime2.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
            var patch8 = JsonDocument.Parse("""{"days":8}""").RootElement;
            await runtime2.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, patch8, null, null));

            using var read = new AppDbContext(_options);
            var managerTask = await read.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_manager")
                .FirstAsync();
            await runtime2.SubmitTaskAsync(new SubmitTaskCommand(managerTask.Id, _eltonId, null, Decision.Approve, "ok"));

            using var read2 = new AppDbContext(_options);
            var vpTask = await read2.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_vp")
                .FirstOrDefaultAsync();
            Assert.NotNull(vpTask);
            // CEO is dept head of CORP and Elton's department head fallback resolves;
            // for Wilson's department (ENG), the dept head is Elton, so approval_vp
            // resolves to Elton via spec path "submitter.department.head".
            Assert.True(vpTask!.ActualAssigneeUserId == _eltonId || vpTask.ActualAssigneeUserId == _ceoId);
        }
    }

    [Fact]
    public async Task ClaimTask_atomic_when_concurrent()
    {
        // Build an instance with a pending Approval whose CandidateSet contains
        // multiple users (HR role) so that one task per candidate exists. Then
        // simulate two parallel claims and assert only one succeeds.

        // Use the LEAVE spec but seed an additional task manually with two
        // siblings sharing the same NodeId so that ClaimTaskAsync's sibling
        // cancellation is exercised.
        var (db, runtime) = BuildRuntime();
        using var _db = db;

        // Seed a fake instance + two sibling pool tasks.
        var inst = new ProcessInstance
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            SpecCode = "LEAVE",
            SpecVersion = 1,
            SpecSnapshotJson = "{}",
            InitiatorUserId = _wilsonId,
            Status = InstanceStatus.Running,
            CurrentFormDataJson = "{}",
            StartedAt = _clock.UtcNow,
            LastActivityAt = _clock.UtcNow,
        };
        var t1 = new ProcessTask
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = inst.Id,
            NodeId = "approval_pool",
            NodeKind = NodeKind.Approval,
            OriginalAssigneeUserId = _eltonId,
            ActualAssigneeUserId = _eltonId,
            Status = TaskStatus.Pending,
        };
        var t2 = new ProcessTask
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = inst.Id,
            NodeId = "approval_pool",
            NodeKind = NodeKind.Approval,
            OriginalAssigneeUserId = _amyId,
            ActualAssigneeUserId = _amyId,
            Status = TaskStatus.Pending,
        };
        db.ProcessInstances.Add(inst);
        db.ProcessTasks.AddRange(t1, t2);
        await db.SaveChangesAsync();

        var (db2, runtime2) = BuildRuntime();
        using var _db2 = db2;

        // Two parallel claim attempts on the SAME task.
        var task1 = runtime.ClaimTaskAsync(new ClaimTaskCommand(t1.Id, _eltonId));
        var task2 = runtime2.ClaimTaskAsync(new ClaimTaskCommand(t1.Id, _eltonId));
        var results = await Task.WhenAll(SafeRun(task1), SafeRun(task2));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, results.Count(r => r is ConflictException));

        using var read = new AppDbContext(_options);
        var fresh1 = await read.ProcessTasks.FirstAsync(x => x.Id == t1.Id);
        var fresh2 = await read.ProcessTasks.FirstAsync(x => x.Id == t2.Id);
        Assert.Equal(TaskStatus.InProgress, fresh1.Status);
        // Sibling should have been auto-cancelled.
        Assert.Equal(TaskStatus.Cancelled, fresh2.Status);
    }

    private static async Task<Exception?> SafeRun(System.Threading.Tasks.Task work)
    {
        try { await work; return null; }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public async Task CancelInstance_cancels_open_tasks()
    {
        var (db, runtime) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));

        await runtime.CancelInstanceAsync(new CancelInstanceCommand(start.InstanceId, _wilsonId, "duplicate"));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
        Assert.Equal("duplicate", instance.CancelReason);

        var openTasks = await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync();
        Assert.Empty(openTasks);

        var cancelHist = await read.TaskHistory
            .Where(h => h.ProcessInstanceId == start.InstanceId && h.EventType == HistoryEventType.InstanceCancelled)
            .FirstOrDefaultAsync();
        Assert.NotNull(cancelHist);
    }
}

internal sealed class NoOpResolutionAuditor : IActorResolutionAuditor
{
    public Task WriteAsync(Bpm.Domain.Spec.ActorRef actor, Bpm.Domain.Spec.ResolutionContext ctx, Bpm.Domain.Spec.ResolutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}

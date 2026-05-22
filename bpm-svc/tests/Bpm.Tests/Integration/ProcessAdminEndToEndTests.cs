using System.Text.Json;
using Bpm.Application.Org;
using Bpm.Application.Process.Reporting;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Simulator;
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
using Bpm.Persistence.Process.Admin;
using Bpm.Persistence.Process.Reporting;
using Bpm.Persistence.Process.Simulator;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Spec;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Integration;

/// <summary>
/// PR-K6 §10 — end-to-end verification suite for the Process Admin UI.
/// Each test boots a fresh in-memory SQLite + a small org seed and exercises
/// one of the admin features through real service implementations:
///
/// <list type="bullet">
///   <item><description><b>10.4</b> — simulator drives the LEAVE spec with an
///   8-day vacation form and asserts the trace routes through
///   <c>approval_vp</c> (the days &gt;= 7 branch).</description></item>
///   <item><description><b>10.5</b> — admin <c>ForceReassignAsync</c> swaps
///   the manager-approval task to a delegate and writes a
///   <c>TaskClaimed</c> history row carrying the
///   <c>{actorRole, originalAssigneeUserId, newAssigneeUserId, reason}</c>
///   payload the audit trail depends on.</description></item>
///   <item><description><b>10.6</b> — reporting service computes
///   <c>BreachCount = 3</c> and <c>BreachRate = 3/5</c> over a deliberately
///   shaped fixture of 5 instances where 3 have a task with
///   <c>DueAt &lt; CompletedAt</c>.</description></item>
/// </list>
///
/// <para>
/// Marked <c>[Trait("category", "integration")]</c> so future CI splits can
/// keep these out of the fast unit-test lane (they each spin up SQLite +
/// seed an org chart). The shape mirrors the PR-I8 BundleE2ETests pattern
/// rather than booting the full WebApplicationFactory — the controller
/// happy/sad paths are covered by the K1–K5 controller tests; this file
/// proves the underlying services compose end-to-end against real DB rows.
/// </para>
/// </summary>
[Trait("category", "integration")]
public sealed class ProcessAdminEndToEndTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private const string SampleSpecsDir = "/Users/jason/claude/bpm/sample_specs";
    private const string SpecCode = "LEAVE";

    // Stable user ids for assertions. We seed a manager/employee/delegate
    // trio plus an HR persona — enough for the LEAVE spec's actor refs to
    // resolve cleanly (submitter.manager + role:HR + submitter.department.head).
    private readonly Guid _adminId    = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private readonly Guid _wilsonId   = Guid.NewGuid(); // initiator
    private readonly Guid _yangId     = Guid.NewGuid(); // manager
    private readonly Guid _linId      = Guid.NewGuid(); // delegate target
    private readonly Guid _maryId     = Guid.NewGuid(); // HR
    private readonly Guid _chenVpId   = Guid.NewGuid(); // VP / dept head
    private readonly Guid _sandyCeoId = Guid.NewGuid(); // CEO
    private readonly Guid _assigneeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _initiatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public ProcessAdminEndToEndTests()
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

    /* ==================================================================
     * 10.4 — Simulator drives the LEAVE spec with an 8-day vacation form
     *        and the trace contains approval_vp on the days >= 7 branch.
     * ================================================================== */

    [Fact]
    public async Task ProcessAdmin_simulator_runs_8_day_vacation_and_traces_VP_path()
    {
        var simulator = BuildSimulator();
        using var form = JsonDocument.Parse("""
            {"leave_type":"事假","date_range":{"start":"2026-06-01","end":"2026-06-10"},"days":8,"reason":"long trip"}
        """);

        var result = await simulator.SimulateAsync(new SimulationRequest(
            FlowCode: SpecCode,
            FormData: form.RootElement.Clone(),
            SampleOrg: null,
            InitiatorUserId: _wilsonId));

        Assert.True(result.Success, $"expected success, got error: {result.Error}");
        Assert.Equal("Completed", result.FinalStatus);

        var nodeIds = result.Trace.Select(s => s.NodeId).ToList();
        // The VP path is what differentiates the 8-day branch from the
        // happy < 7-day path (which skips approval_vp). This is the smoke
        // assertion the brief calls out specifically.
        Assert.Contains("approval_vp", nodeIds);

        // Spot-check the canonical sequence — the simulator may insert a
        // synthetic terminal row at the tail, so we assert relative order
        // of the spec's user-facing nodes rather than exact indexes.
        var expectedOrder = new[]
        {
            "task_apply", "approval_manager", "approval_vp", "task_hr_archive",
        };
        var lastIndex = -1;
        foreach (var nodeId in expectedOrder)
        {
            var idx = nodeIds.IndexOf(nodeId);
            Assert.True(idx > lastIndex,
                $"expected '{nodeId}' to appear after index {lastIndex} in trace; got: [{string.Join(", ", nodeIds)}]");
            lastIndex = idx;
        }
    }

    /* ==================================================================
     * 10.5 — Force-reassign writes an admin-actor TaskHistory row with the
     *        full {actorRole, original, new, reason} payload.
     * ================================================================== */

    [Fact]
    public async Task ProcessAdmin_force_reassign_writes_admin_actor_history()
    {
        // Drive a LEAVE instance through Wilson's apply submit so the
        // manager-approval task is open and assigned to Yang.
        var (instanceId, taskId) = await StartLeaveAndOpenManagerTaskAsync();

        // Build the intervention service against its own DbContext (mirrors
        // production DI scoping; reusing the runtime's would entangle
        // change-tracker state with SubmitTaskAsync's per-call transaction).
        await using var svcDb = new AppDbContext(_options);
        var (_, runtime, _) = BuildRuntime();
        var intervention = new ProcessAdminInterventionService(svcDb, _clock, runtime);

        await intervention.ForceReassignAsync(taskId, _linId, "Yang on leave", _adminId);

        // Assertion 1: task state — assignee swapped, claim cleared, status reset.
        await using var read = new AppDbContext(_options);
        var task = await read.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == taskId);
        Assert.Equal(_linId, task.ActualAssigneeUserId);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.ClaimedAt);

        // Assertion 2: history row exists with admin actor + the K4 contract payload.
        var history = await read.TaskHistory.AsNoTracking()
            .Where(h => h.TaskId == taskId
                        && h.EventType == HistoryEventType.TaskClaimed
                        && h.ActorUserId == _adminId)
            .FirstOrDefaultAsync();
        Assert.NotNull(history);

        using var payload = JsonDocument.Parse(history!.PayloadJson);
        Assert.Equal("admin", payload.RootElement.GetProperty("actorRole").GetString());
        Assert.Equal("Yang on leave", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal(_yangId, payload.RootElement.GetProperty("originalAssigneeUserId").GetGuid());
        Assert.Equal(_linId, payload.RootElement.GetProperty("newAssigneeUserId").GetGuid());
    }

    /* ==================================================================
     * 10.6 — Reports breach count + rate match the DB-level shape.
     *        Fixture: 5 instances; 3 have a breach task (CompletedAt > DueAt).
     * ================================================================== */

    [Fact]
    public async Task ProcessAdmin_reports_breach_rate_matches_db_count()
    {
        // Wipe any rows the org seeder added that aren't relevant here, and
        // make sure the dedicated reporting fixture users exist (the seed
        // step creates them for the simulator path; this test only needs them
        // as referenced FKs on the seeded process tasks).
        await EnsureReportingUsersAsync();

        // Build a deterministic fixture: 5 completed LEAVE instances; 3 of
        // them have a "breach" task (CompletedAt > DueAt). The other 2 have
        // tasks completed before the SLA — i.e. NOT breached.
        for (var i = 0; i < 3; i++)
        {
            var instanceId = await SeedCompletedInstanceAsync();
            await SeedCompletedBreachTaskAsync(instanceId, hours: 2, dueAtOffsetHours: -1);
        }
        for (var i = 0; i < 2; i++)
        {
            var instanceId = await SeedCompletedInstanceAsync();
            await SeedCompletedNonBreachTaskAsync(instanceId, hours: 2, dueAtOffsetHours: 24);
        }

        // Sanity: DB-level breach shape matches what we asked for.
        await using var verify = new AppDbContext(_options);
        var rawInstanceCount = await verify.ProcessInstances
            .Where(i => i.SpecCode == SpecCode && i.Status == InstanceStatus.Completed)
            .CountAsync();
        Assert.Equal(5, rawInstanceCount);
        var rawBreachCount = await verify.ProcessTasks.AsNoTracking()
            .Where(t => t.Status == TaskStatus.Completed
                        && t.DueAt != null
                        && t.CompletedAt != null
                        && t.CompletedAt > t.DueAt)
            .CountAsync();
        Assert.Equal(3, rawBreachCount);

        // Now go through the reporting service.
        var report = await BuildReporting().GetPerSpecReportAsync(SpecCode, ReportPeriod.Last30Days);

        Assert.Equal(5, report.TotalCompleted);
        Assert.Equal(3, report.BreachCount);
        Assert.Equal(3.0 / 5.0, report.BreachRate, precision: 6);
    }

    /* ────────────────────────────────────────────────────────────── */

    private void SeedOrg(AppDbContext db)
    {
        var corp = new Department { Id = Guid.NewGuid(), Code = "CORP", Name = "Corporate", DisplayName = "Corporate" };
        var eng  = new Department { Id = Guid.NewGuid(), Code = "ENG",  Name = "Engineering", DisplayName = "Engineering", ParentId = corp.Id };

        var sandy  = new User { Id = _sandyCeoId, DisplayName = "Sandy",  Email = "ceo@e2e",  FullName = "Sandy (CEO)", DepartmentId = corp.Id };
        var chen   = new User { Id = _chenVpId,   DisplayName = "Chen",   Email = "vp@e2e",   FullName = "Chen (VP)",   DepartmentId = corp.Id, ManagerId = sandy.Id };
        var yang   = new User { Id = _yangId,     DisplayName = "Yang",   Email = "mgr@e2e",  FullName = "Yang (Mgr)",  DepartmentId = eng.Id,  ManagerId = chen.Id };
        var wilson = new User { Id = _wilsonId,   DisplayName = "Wilson", Email = "emp@e2e",  FullName = "Wilson",      DepartmentId = eng.Id,  ManagerId = yang.Id };
        var lin    = new User { Id = _linId,      DisplayName = "Lin",    Email = "lin@e2e",  FullName = "Lin",         DepartmentId = eng.Id,  ManagerId = yang.Id };
        var mary   = new User { Id = _maryId,     DisplayName = "Mary",   Email = "hr@e2e",   FullName = "Mary (HR)",   DepartmentId = corp.Id, ManagerId = sandy.Id };

        db.Departments.AddRange(corp, eng);
        db.Users.AddRange(sandy, chen, yang, wilson, lin, mary);

        var hrRole = new Role { Id = Guid.NewGuid(), Code = "HR", Name = "Human Resources", Scope = RoleScope.System };
        var vpRole = new Role { Id = Guid.NewGuid(), Code = "VP", Name = "Vice President",  Scope = RoleScope.System };
        db.Roles.AddRange(hrRole, vpRole);
        db.SaveChanges();

        // Department heads — ENG.head = Chen so submitter.department.head
        // resolves to a person distinct from Wilson's immediate manager.
        corp.HeadUserId = sandy.Id;
        eng.HeadUserId  = chen.Id;

        db.RoleAssignments.AddRange(
            new RoleAssignment { RoleId = hrRole.Id, PrincipalId = mary.Id },
            new RoleAssignment { RoleId = vpRole.Id, PrincipalId = chen.Id });
        db.SaveChanges();
    }

    private async Task EnsureReportingUsersAsync()
    {
        await using var db = new AppDbContext(_options);
        if (!await db.Users.AnyAsync(u => u.Id == _initiatorId))
        {
            db.Users.Add(new User
            {
                Id = _initiatorId, DisplayName = "init", Email = "init@e2e", FullName = "Init", IsActive = true,
            });
        }
        if (!await db.Users.AnyAsync(u => u.Id == _assigneeId))
        {
            db.Users.Add(new User
            {
                Id = _assigneeId, DisplayName = "asg", Email = "asg@e2e", FullName = "Asg", IsActive = true,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<(Guid instanceId, Guid taskId)> StartLeaveAndOpenManagerTaskAsync()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand(SpecCode, form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var managerTask = await db.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && t.NodeId == "approval_manager"
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();
        return (start.InstanceId, managerTask.Id);
    }

    private async Task<Guid> SeedCompletedInstanceAsync()
    {
        await using var db = new AppDbContext(_options);
        var id = Guid.NewGuid();
        db.ProcessInstances.Add(new ProcessInstance
        {
            Id = id,
            SpecCode = SpecCode,
            SpecVersion = 1,
            InitiatorUserId = _initiatorId,
            Status = InstanceStatus.Completed,
            StartedAt = _clock.UtcNow.AddHours(-3),
            CompletedAt = _clock.UtcNow,
            LastActivityAt = _clock.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedCompletedBreachTaskAsync(Guid instanceId, double hours, double dueAtOffsetHours)
    {
        await SeedCompletedTaskAsync(instanceId, "approval_manager", hours, dueAtOffsetHours);
    }

    private async Task SeedCompletedNonBreachTaskAsync(Guid instanceId, double hours, double dueAtOffsetHours)
    {
        await SeedCompletedTaskAsync(instanceId, "approval_manager", hours, dueAtOffsetHours);
    }

    private async Task SeedCompletedTaskAsync(Guid instanceId, string nodeId, double hours, double dueAtOffsetHours)
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
            // dueAtOffsetHours is measured FROM createdAt — negative means
            // the SLA window had already passed by the time the task was
            // completed (a breach), positive means there was still slack.
            DueAt = createdAt.AddHours(dueAtOffsetHours),
        };
        db.ProcessTasks.Add(task);
        await db.SaveChangesAsync();

        // The audit interceptor stamps CreatedAt = now on Add. Pin it back
        // via ExecuteUpdate (interceptor only touches tracked entries).
        var taskId = task.Id;
        await db.ProcessTasks.Where(x => x.Id == taskId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, createdAt));
    }

    /* ── service builders ────────────────────────────────────────── */

    private (AppDbContext db, ProcessRuntime runtime, SandboxCapturingNotificationDispatcher dispatcher) BuildRuntime()
    {
        var db = new AppDbContext(_options);
        var orgReader = new OrgChartReader(db);
        var auditor = new NoOpAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var gate = new OutboundGate(db, _clock);
        var dispatcher = new SandboxCapturingNotificationDispatcher(
            gate, db, NullLogger<SandboxCapturingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var loader = new FileSystemSpecLoader(BuildConfig(), NullLogger<FileSystemSpecLoader>.Instance);
        var runtime = new ProcessRuntime(db, _clock, loader, resolver, delegation, dispatcher,
            evaluator, NullLogger<ProcessRuntime>.Instance);
        return (db, runtime, dispatcher);
    }

    private ProcessSimulator BuildSimulator()
    {
        var simDb = new AppDbContext(_options);
        var (_, runtime, _) = BuildRuntime();
        var loader = new FileSystemSpecLoader(BuildConfig(), NullLogger<FileSystemSpecLoader>.Instance);
        return new ProcessSimulator(simDb, runtime, loader, NullLogger<ProcessSimulator>.Instance);
    }

    private IProcessReportingService BuildReporting()
    {
        var db = new AppDbContext(_options);
        return new ProcessReportingService(db, _clock);
    }

    private IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bpm:SampleSpecsDir"] = SampleSpecsDir,
        })
        .Build();

    /// <summary>
    /// Local stub — the existing NoOpResolutionAuditor in
    /// <c>Bpm.Tests.Persistence.Spec.Bundle</c> is internal and lives in a
    /// different test namespace; re-using it here would require either
    /// promoting it to a Common helper or duplicating its using-block. We
    /// duplicate the 3-line shape here to keep the integration file
    /// self-contained.
    /// </summary>
    private sealed class NoOpAuditor : Bpm.Application.Spec.IActorResolutionAuditor
    {
        public Task WriteAsync(
            Bpm.Domain.Spec.ActorRef actor,
            Bpm.Domain.Spec.ResolutionContext ctx,
            Bpm.Domain.Spec.ResolutionResult result,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}

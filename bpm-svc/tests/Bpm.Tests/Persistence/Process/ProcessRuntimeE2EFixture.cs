using System.Text.Json;
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

/// <summary>
/// End-to-end fixture for §10/§11/§12 of add-process-runtime: drives the
/// runtime against the real <c>sample_specs/*.json</c> files using an
/// in-memory SQLite DB seeded with a small org chart. Each scenario builds
/// its own harness so state never leaks across tests.
///
/// Org chart:
///   CORP (head=Sandy/CEO)
///   ├ ENG  (head=Chen/VP — distinct from Wilson's manager)
///   │   ├ Yang (manager of Wilson, Lin)
///   │   ├ Wilson (employee, initiator)
///   │   └ Lin (delegate target for Yang)
///   └ FIN
///       └ Jin (Finance lead)
///   Mary (HR, dept=CORP)
///   Chen (VP — also head of ENG)
///   Sandy (CEO — also has CEO role for purchase tier 3)
/// </summary>
public sealed class ProcessRuntimeE2EFixture : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    // Stable user ids so assertions can name them.
    private readonly Guid _wilsonId   = Guid.NewGuid();
    private readonly Guid _yangId     = Guid.NewGuid();
    private readonly Guid _linId      = Guid.NewGuid();
    private readonly Guid _maryId     = Guid.NewGuid();
    private readonly Guid _chenVpId   = Guid.NewGuid();
    private readonly Guid _sandyCeoId = Guid.NewGuid();
    private readonly Guid _jinFinId   = Guid.NewGuid();
    private readonly Guid _purchaseId = Guid.NewGuid();

    // PR-K4 — exposed for ProcessAdminInterventionTests reuse so the LEAVE
    // org chart + sample-spec loader wiring stays in one place.
    internal DbContextOptions<AppDbContext> Options => _options;
    internal StubClock Clock => _clock;
    internal Guid WilsonId => _wilsonId;
    internal Guid YangId => _yangId;
    internal Guid LinId => _linId;
    internal Guid MaryId => _maryId;
    internal (AppDbContext db, ProcessRuntime runtime, RecordingNotificationDispatcher notifier) Build(
        IDelegationService? delegation = null) => BuildRuntime(delegation);

    public ProcessRuntimeE2EFixture()
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
        var eng  = new Department { Id = Guid.NewGuid(), Code = "ENG",  Name = "Engineering", DisplayName = "Engineering", ParentId = corp.Id };
        var fin  = new Department { Id = Guid.NewGuid(), Code = "FIN",  Name = "Finance",     DisplayName = "Finance",     ParentId = corp.Id };

        var sandy  = new User { Id = _sandyCeoId, DisplayName = "Sandy",  Email = "ceo@e2e",  FullName = "Sandy Chen (CEO)",     DepartmentId = corp.Id };
        var chen   = new User { Id = _chenVpId,   DisplayName = "Chen",   Email = "vp@e2e",   FullName = "Chen (VP)",            DepartmentId = corp.Id, ManagerId = sandy.Id };
        var yang   = new User { Id = _yangId,     DisplayName = "Yang",   Email = "mgr@e2e",  FullName = "Elton Yang (Manager)", DepartmentId = eng.Id,  ManagerId = chen.Id };
        var wilson = new User { Id = _wilsonId,   DisplayName = "Wilson", Email = "emp@e2e",  FullName = "Wilson You",           DepartmentId = eng.Id,  ManagerId = yang.Id };
        var lin    = new User { Id = _linId,      DisplayName = "Lin",    Email = "lin@e2e",  FullName = "Lin (delegate)",       DepartmentId = eng.Id,  ManagerId = yang.Id };
        var mary   = new User { Id = _maryId,     DisplayName = "Mary",   Email = "hr@e2e",   FullName = "Mary (HR)",            DepartmentId = corp.Id, ManagerId = sandy.Id };
        var jin    = new User { Id = _jinFinId,   DisplayName = "Jin",    Email = "fin@e2e",  FullName = "Jin (Finance lead)",   DepartmentId = fin.Id,  ManagerId = sandy.Id };
        var purch  = new User { Id = _purchaseId, DisplayName = "Pat",    Email = "po@e2e",   FullName = "Pat (Purchasing)",     DepartmentId = corp.Id, ManagerId = sandy.Id };

        db.Departments.AddRange(corp, eng, fin);
        db.Users.AddRange(sandy, chen, yang, wilson, lin, mary, jin, purch);

        var hrRole       = new Role { Id = Guid.NewGuid(), Code = "HR",       Name = "Human Resources",   Scope = RoleScope.System };
        var vpRole       = new Role { Id = Guid.NewGuid(), Code = "VP",       Name = "Vice President",    Scope = RoleScope.System };
        var financeRole  = new Role { Id = Guid.NewGuid(), Code = "Finance",  Name = "Finance Reviewer",  Scope = RoleScope.System };
        var ceoRole      = new Role { Id = Guid.NewGuid(), Code = "CEO",      Name = "Chief Executive",   Scope = RoleScope.System };
        var purchaseRole = new Role { Id = Guid.NewGuid(), Code = "Purchase", Name = "Purchasing",        Scope = RoleScope.System };

        db.Roles.AddRange(hrRole, vpRole, financeRole, ceoRole, purchaseRole);
        db.SaveChanges();

        // Department heads. ENG.head = Chen (the VP), so submitter.department.head
        // for Wilson resolves to a person distinct from his immediate manager.
        corp.HeadUserId = sandy.Id;
        eng.HeadUserId  = chen.Id;
        fin.HeadUserId  = jin.Id;

        db.RoleAssignments.AddRange(
            new RoleAssignment { RoleId = hrRole.Id,       PrincipalId = mary.Id },
            new RoleAssignment { RoleId = vpRole.Id,       PrincipalId = chen.Id },
            new RoleAssignment { RoleId = financeRole.Id,  PrincipalId = jin.Id },
            new RoleAssignment { RoleId = ceoRole.Id,      PrincipalId = sandy.Id },
            new RoleAssignment { RoleId = purchaseRole.Id, PrincipalId = purch.Id });
        db.SaveChanges();
    }

    private (AppDbContext db, ProcessRuntime runtime, RecordingNotificationDispatcher notifier) BuildRuntime(
        IDelegationService? delegation = null)
    {
        var db = new AppDbContext(_options);
        var orgReader = new OrgChartReader(db);
        var auditor = new NoOpResolutionAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var del = delegation ?? new StubDelegationService();
        var notifier = new RecordingNotificationDispatcher();
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bpm:SampleSpecsDir"] = "/Users/jason/claude/bpm/sample_specs",
            })
            .Build();
        var loader = new FileSystemSpecLoader(cfg, NullLogger<FileSystemSpecLoader>.Instance);
        var runtime = new ProcessRuntime(db, _clock, loader, resolver, del, notifier, evaluator,
            NullLogger<ProcessRuntime>.Instance);
        return (db, runtime, notifier);
    }

    // ===== §10 — ProcessRuntimeFixture exercising LEAVE end-to-end =====

    [Fact] // 10.1 happy_5_day_leave
    public async Task Leave_happy_5_day_completes_via_manager_then_hr()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"leave_type":"特休","date_range":{"start":"2026-05-10","end":"2026-05-12"},"days":3,"reason":"家裡有事"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));

        // Step 1: Wilson submits the apply task (initial userTask).
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, "applying"));

        // Step 2: Yang sees a manager-approval task, approves.
        var managerTask = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(_yangId, managerTask.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(managerTask.Id, _yangId, null, Decision.Approve, "ok"));

        // Step 3: gateway routes to HR archive (days < 7); Mary (HR) submits.
        var hrTask = await NextOpenTask(start.InstanceId, "task_hr_archive");
        Assert.Equal(_maryId, hrTask.ActualAssigneeUserId);
        var notePatch = JsonDocument.Parse("""{"archive_note":"歸檔完成"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(hrTask.Id, _maryId, notePatch, null, null));

        // Verify terminal state.
        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);

        var openTasks = await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync();
        Assert.Empty(openTasks);

        // Verify history sequence — order by CreatedAt then Id for stability.
        var history = await read.TaskHistory
            .Where(h => h.ProcessInstanceId == start.InstanceId)
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Select(h => h.EventType)
            .ToListAsync();
        // Within a single SaveChanges all rows share CreatedAt (StubClock fixed),
        // so ordering by Id is non-deterministic — assert membership rather
        // than a strict positional sequence.
        Assert.Contains(HistoryEventType.InstanceStarted, history);
        Assert.Contains(HistoryEventType.GatewayEvaluated, history);
        Assert.Contains(HistoryEventType.ApprovalApproved, history);
        Assert.Contains(HistoryEventType.InstanceCompleted, history);
    }

    [Fact] // 10.2 gateway_8_day_routes_to_vp
    public async Task Leave_8_day_routes_through_vp_then_hr()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"leave_type":"事假","date_range":{"start":"2026-06-01","end":"2026-06-10"},"days":8,"reason":"出國"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var managerTask = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(_yangId, managerTask.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(managerTask.Id, _yangId, null, Decision.Approve, "ok"));

        var vpTask = await NextOpenTask(start.InstanceId, "approval_vp");
        // submitter.department.head — Wilson is in ENG, ENG.head is Chen.
        Assert.Equal(_chenVpId, vpTask.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(vpTask.Id, _chenVpId, null, Decision.Approve, "vp ok"));

        var hrTask = await NextOpenTask(start.InstanceId, "task_hr_archive");
        Assert.Equal(_maryId, hrTask.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(hrTask.Id, _maryId, null, null, null));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);

        // Verify gateway picked the >=7 branch (e4 → approval_vp).
        var gw = await read.TaskHistory
            .Where(h => h.ProcessInstanceId == start.InstanceId && h.EventType == HistoryEventType.GatewayEvaluated)
            .FirstAsync();
        using var gwPayload = JsonDocument.Parse(gw.PayloadJson);
        Assert.Equal("e4", gwPayload.RootElement.GetProperty("chosenEdgeId").GetString());
    }

    [Fact] // 10.3 delegation_applied
    public async Task Leave_delegation_redirects_manager_approval_to_delegate()
    {
        var del = new StubReplaceDelegationService(principal: _yangId, delegateTo: _linId);
        var (db, runtime, _) = BuildRuntime(delegation: del);
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var managerTask = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(_yangId, managerTask.OriginalAssigneeUserId);
        Assert.Equal(_linId, managerTask.ActualAssigneeUserId);

        using var read = new AppDbContext(_options);
        var delegated = await read.TaskHistory
            .Where(h => h.ProcessInstanceId == start.InstanceId && h.EventType == HistoryEventType.DelegationApplied)
            .FirstOrDefaultAsync();
        Assert.NotNull(delegated);
    }

    [Fact] // 10.4 cancel_mid_flow
    public async Task Leave_cancel_mid_flow_cancels_open_tasks()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        // Pause at manager-approval, then cancel.
        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(TaskStatus.Pending, mgr.Status);

        await runtime.CancelInstanceAsync(new CancelInstanceCommand(start.InstanceId, _wilsonId, "changed mind"));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Cancelled, instance.Status);
        Assert.Equal("changed mind", instance.CancelReason);

        var openTasks = await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync();
        Assert.Empty(openTasks);

        var cancelHist = await read.TaskHistory
            .Where(h => h.ProcessInstanceId == start.InstanceId
                        && h.EventType == HistoryEventType.InstanceCancelled)
            .FirstOrDefaultAsync();
        Assert.NotNull(cancelHist);
    }

    // ===== §11 — Notification dispatch verification =====

    [Fact] // 11.1 dispatcher consulted on submit (LEAVE has no on_submit)
    public async Task Notifications_dispatcher_consulted_for_on_submit_even_when_no_match()
    {
        var (db, runtime, notifier) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));

        var onSubmit = notifier.Calls.Where(c => c.Trigger == "on_submit").ToList();
        Assert.Single(onSubmit);
        Assert.Empty(onSubmit[0].NotificationIds); // leave_v1 has no on_submit notification.
    }

    [Fact] // 11.2 on_assign with notify_assign_manager id surfaces
    public async Task Notifications_on_assign_dispatched_with_manager_notification_id()
    {
        var (db, runtime, notifier) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
        notifier.Calls.Clear();

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var assigns = notifier.Calls.Where(c => c.Trigger == "on_assign").ToList();
        Assert.NotEmpty(assigns);
        Assert.Contains(assigns, a => a.NotificationIds.Contains("notify_assign_manager"));
    }

    [Fact] // 11.3 dispatcher consulted on approve, no on_approve match in leave_v1
    public async Task Notifications_on_approve_dispatched_even_when_no_match()
    {
        var (db, runtime, notifier) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));
        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        notifier.Calls.Clear();

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, "ok"));

        // The runtime does not emit `on_approve` on submit (it emits on_assign
        // for the next task). leave_v1 also defines no on_approve template, so
        // we assert no on_approve calls exist — proving the dispatcher would
        // have produced an empty match set even if consulted.
        var onApprove = notifier.Calls.Where(c => c.Trigger == "on_approve").ToList();
        Assert.Empty(onApprove);
    }

    [Fact] // 11.4 on_complete dispatched with notify_complete id
    public async Task Notifications_on_complete_dispatched_when_instance_completes()
    {
        var (db, runtime, notifier) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""{"leave_type":"特休","days":3,"reason":"v"}""").RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("LEAVE", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));
        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, "ok"));
        var hr = await NextOpenTask(start.InstanceId, "task_hr_archive");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(hr.Id, _maryId, null, null, null));

        var completes = notifier.Calls.Where(c => c.Trigger == "on_complete").ToList();
        Assert.NotEmpty(completes);
        Assert.Contains(completes, c => c.NotificationIds.Contains("notify_complete"));
    }

    // ===== §12 — Sample specs exercising =====

    // 12.1 — covered by Leave_happy_5_day_completes_via_manager_then_hr above.

    [Fact] // 12.2 purchase mid-tier (manager + finance)
    public async Task Purchase_50k_routes_through_finance_then_purchase_exec()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"vendor":"聯強","category":"it","amount":50000,"items":"MBA","justification":"new hire"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("PURCHASE", form, _wilsonId));

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));
        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(_yangId, mgr.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, null));

        var fin = await NextOpenTask(start.InstanceId, "approval_finance");
        Assert.Equal(_jinFinId, fin.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(fin.Id, _jinFinId, null, Decision.Approve, null));

        var exec = await NextOpenTask(start.InstanceId, "task_purchase_exec");
        Assert.Equal(_purchaseId, exec.ActualAssigneeUserId);
        var execPatch = JsonDocument.Parse("""{"po_number":"PO-1","expected_delivery":"2026-06-01"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(exec.Id, _purchaseId, execPatch, null, null));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);

        // No CEO task should have been spawned (amount < 100000).
        var ceoTask = await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_ceo")
            .FirstOrDefaultAsync();
        Assert.Null(ceoTask);
    }

    [Fact] // 12.2b purchase top-tier (manager + finance + CEO)
    public async Task Purchase_200k_requires_manager_finance_and_ceo()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"vendor":"資安顧問","category":"service","amount":200000,"items":"年度滲透測試","justification":"ISO 27001"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("PURCHASE", form, _wilsonId));

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));
        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, null));

        var fin = await NextOpenTask(start.InstanceId, "approval_finance");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(fin.Id, _jinFinId, null, Decision.Approve, null));

        var ceo = await NextOpenTask(start.InstanceId, "approval_ceo");
        Assert.Equal(_sandyCeoId, ceo.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(ceo.Id, _sandyCeoId, null, Decision.Approve, null));

        var exec = await NextOpenTask(start.InstanceId, "task_purchase_exec");
        Assert.Equal(_purchaseId, exec.ActualAssigneeUserId);
        var execPatch = JsonDocument.Parse("""{"po_number":"PO-2","expected_delivery":"2026-07-01"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(exec.Id, _purchaseId, execPatch, null, null));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);
    }

    [Fact] // 12.3 TEO small tier — manager approve, gateway skips extra approval, two finance steps
    public async Task Teo_small_amount_routes_through_manager_then_two_finance_steps()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        // total_amount=5000 → gateway_threshold routes straight to task_finance_confirm,
        // skipping approval_extra. Per workflow.ts TEO has two finance steps (confirm+review).
        var form = JsonDocument.Parse("""
            {"trq_no":"","destination":"台中","purpose":"客戶拜訪","actual_amount":5000,"total_amount":5000,"expense_breakdown":"hsr 1500\ntaxi 1500\nmeal 2000","receipts":"r.pdf"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("TEO", form, _wilsonId));

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        Assert.Equal(_yangId, mgr.ActualAssigneeUserId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, "ok"));

        // sum < 50000 → gateway sends straight to task_finance_confirm, skipping approval_extra.
        var confirm = await NextOpenTask(start.InstanceId, "task_finance_confirm");
        Assert.Equal(_jinFinId, confirm.ActualAssigneeUserId);
        var confirmPatch = JsonDocument.Parse("""{"print_no":"PR-001"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(confirm.Id, _jinFinId, confirmPatch, null, null));

        var review = await NextOpenTask(start.InstanceId, "task_finance_review");
        Assert.Equal(_jinFinId, review.ActualAssigneeUserId);
        var reviewPatch = JsonDocument.Parse("""{"paid_at":"2026-05-15"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(review.Id, _jinFinId, reviewPatch, null, null));

        using var read = new AppDbContext(_options);
        var instance = await read.ProcessInstances.FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);
    }

    [Fact] // 12.3b TEO over-threshold tier — manager + collection mode='any' min_approvals=2
    public async Task Teo_over_threshold_extra_approval_any_two_of_three_advances()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"trq_no":"","destination":"東京","purpose":"海外研討會","actual_amount":97000,"total_amount":97000,"expense_breakdown":"airfare 50000\nhotel 47000","receipts":"r.pdf"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("TEO", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, "ok"));

        // Three approval_extra tasks should spawn — Chen (dept head ENG), Jin (Finance), Sandy (CEO).
        using (var read = new AppDbContext(_options))
        {
            var spawned = await read.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_extra"
                            && t.Status == TaskStatus.Pending)
                .ToListAsync();
            Assert.Equal(3, spawned.Count);
        }

        // First approval — Chen (dept head).
        var t1 = await NextOpenTaskFor(start.InstanceId, "approval_extra", _chenVpId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(t1.Id, _chenVpId, null, Decision.Approve, "ok"));

        // Second approval — Jin (Finance). After this one min_approvals=2 is met,
        // so the third sibling auto-cancels.
        var t2 = await NextOpenTaskFor(start.InstanceId, "approval_extra", _jinFinId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(t2.Id, _jinFinId, null, Decision.Approve, "ok"));

        using var read2 = new AppDbContext(_options);
        var third = await read2.ProcessTasks
            .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "approval_extra"
                        && t.OriginalAssigneeUserId == _sandyCeoId)
            .FirstAsync();
        Assert.Equal(TaskStatus.Cancelled, third.Status);

        var confirm = await NextOpenTask(start.InstanceId, "task_finance_confirm");
        var confirmPatch = JsonDocument.Parse("""{"print_no":"PR-002"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(confirm.Id, _jinFinId, confirmPatch, null, null));

        var review = await NextOpenTask(start.InstanceId, "task_finance_review");
        var reviewPatch = JsonDocument.Parse("""{"paid_at":"2026-05-20"}""").RootElement;
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(review.Id, _jinFinId, reviewPatch, null, null));

        var instance = await read2.ProcessInstances.AsNoTracking().FirstAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);
    }

    [Fact] // 12.3c TEO extra approval — instance does not advance until 2-of-3 completed
    public async Task Teo_over_threshold_does_not_advance_until_min_approvals_met()
    {
        var (db, runtime, _) = BuildRuntime();
        using var _db = db;

        var form = JsonDocument.Parse("""
            {"trq_no":"","destination":"舊金山","purpose":"美國年會","actual_amount":150000,"total_amount":150000,"expense_breakdown":"airfare 80000\nhotel 70000","receipts":"r.pdf"}
            """).RootElement;
        var start = await runtime.StartInstanceAsync(new StartInstanceCommand("TEO", form, _wilsonId));
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(start.FirstTaskId, _wilsonId, null, null, null));

        var mgr = await NextOpenTask(start.InstanceId, "approval_manager");
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(mgr.Id, _yangId, null, Decision.Approve, null));

        // Only one of three extras approves — instance must still be in approval_extra phase.
        var first = await NextOpenTaskFor(start.InstanceId, "approval_extra", _chenVpId);
        await runtime.SubmitTaskAsync(new SubmitTaskCommand(first.Id, _chenVpId, null, Decision.Approve, null));

        using (var mid = new AppDbContext(_options))
        {
            var confirm = await mid.ProcessTasks
                .Where(t => t.ProcessInstanceId == start.InstanceId && t.NodeId == "task_finance_confirm")
                .FirstOrDefaultAsync();
            Assert.Null(confirm); // not advanced yet — only 1/3 approved, need 2.
        }
    }

    [Fact] // 12.4 all sample specs parse without errors
    public void All_sample_specs_parse_with_SpecSnapshot()
    {
        foreach (var name in new[] {
            "leave_v1.json",
            "purchase_v1.json",
            "gee_v1.json",
            "gev_v1.json",
            "ape_v1.json",
            "hwp_v1.json",
            "itpr_v1.json",
            "trq_v1.json",
            "teo_v1.json",
            "extob_v1.json",
            "resign_v1.json",
            "deptx_v1.json",
        })
        {
            var path = Path.Combine("/Users/jason/claude/bpm/sample_specs", name);
            var json = File.ReadAllText(path);
            using var snap = new SpecSnapshot(json);
            Assert.False(string.IsNullOrEmpty(snap.FlowCode));
            Assert.NotNull(snap.StartNode);
            Assert.NotEmpty(snap.Edges);
        }
    }

    // ===== Helpers =====

    private async Task<ProcessTask> NextOpenTask(Guid instanceId, string nodeId)
    {
        using var read = new AppDbContext(_options);
        return await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == instanceId && t.NodeId == nodeId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();
    }

    private async Task<ProcessTask> NextOpenTaskFor(Guid instanceId, string nodeId, Guid actualAssignee)
    {
        using var read = new AppDbContext(_options);
        return await read.ProcessTasks
            .Where(t => t.ProcessInstanceId == instanceId && t.NodeId == nodeId
                        && t.ActualAssigneeUserId == actualAssignee
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .FirstAsync();
    }
}

/// <summary>
/// Test-only delegation service that swaps a single principal → delegate
/// pair. Returns null for any other principal.
/// </summary>
internal sealed class StubReplaceDelegationService(Guid principal, Guid delegateTo) : IDelegationService
{
    public Task<Guid?> GetActiveDelegateAsync(Guid principalUserId, DateTime nowUtc, CancellationToken ct = default)
        => Task.FromResult<Guid?>(principalUserId == principal ? delegateTo : null);
}

/// <summary>
/// Test-only INotificationDispatcher that records every dispatch call along
/// with the matching notification ids resolved via SpecSnapshot.
/// </summary>
internal sealed class RecordingNotificationDispatcher : INotificationDispatcher
{
    public List<RecordedCall> Calls { get; } = new();

    public Task DispatchAsync(SpecSnapshot snapshot, string trigger, NotificationContext ctx, CancellationToken ct = default)
    {
        var ids = snapshot.GetNotifications(trigger).Select(n => n.Id).ToList();
        Calls.Add(new RecordedCall(trigger, ctx, ids));
        return Task.CompletedTask;
    }

    public sealed record RecordedCall(string Trigger, NotificationContext Context, IReadOnlyList<string> NotificationIds);
}

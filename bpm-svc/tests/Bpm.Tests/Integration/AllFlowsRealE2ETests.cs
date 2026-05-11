using System.Text.Json;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Seed;
using Bpm.Persistence.Spec;
using Bpm.Tests.Common;
// NoOpResolutionAuditor lives in BundleReproducibilityRunnerTests.
using Bpm.Tests.Persistence.Spec.Bundle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Integration;

/// <summary>
/// PR-L5 — single end-to-end acceptance suite proving every demo flow runs
/// through <see cref="ProcessRuntime"/> the same way the UI drives it
/// (StartInstanceAsync → SubmitTaskAsync → ... → InstanceStatus.Completed).
///
/// <para>
/// Per <c>docs/all-flows-real-plan.md</c> §PR-L5: each FormCode in the 11
/// demo flows gets a happy-path test plus a reject-path test. EXTOB only
/// has happy (no approval node). TEO has two happies (under / over the 50k
/// threshold). Total: 22 sub-tests.
/// </para>
///
/// <para>
/// Mirrors <see cref="SandboxAcceptanceLoopTests"/> + <see cref="BundleE2ETests"/>:
/// in-memory SQLite, hand-built DI, no <c>WebApplicationFactory</c>. Each
/// test seeds the canonical 13-user persona shape via
/// <see cref="PersonaSeedService"/>, flips sandbox mode on, and uses the
/// real <see cref="FileSystemSpecLoader"/> to read <c>sample_specs/*.json</c>.
/// </para>
///
/// <para>
/// Sandbox-on means <see cref="SandboxCapturingNotificationDispatcher"/> writes
/// every dispatched email into <c>SandboxCapturedMessages</c>; the assertion
/// "captured count &gt; 0" proves the on_assign / on_complete notifications
/// fired through the same OutboundGate the production path uses.
/// </para>
/// </summary>
[Trait("category", "integration")]
public sealed class AllFlowsRealE2ETests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private readonly Dictionary<string, Guid> _userByEmail = new(StringComparer.OrdinalIgnoreCase);

    public AllFlowsRealE2ETests()
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
        // Real persona seed (13 users / 6 departments / role assignments) — same
        // path Program.cs and SeedCli use, so the test exercises the production
        // routing graph rather than a bespoke mini-org.
        PersonaSeedService.RunAsync(db, NullLogger.Instance).GetAwaiter().GetResult();

        foreach (var u in db.Users.AsNoTracking().ToList())
            _userByEmail[u.Email] = u.Id;
    }

    public void Dispose() => _conn.Dispose();

    // ===== LEAVE — employee → manager → (gateway) → hr =====

    [Fact, Trait("flow", "LEAVE")]
    public async Task LEAVE_happy_path_runs_to_completion()
        => await RunHappyAsync("LEAVE", "wilson@acme.test", JsonDocument.Parse("""
            {"leave_type":"特休","date_range":{"start":"2026-05-10","end":"2026-05-12"},"days":3,"reason":"家裡有事"}
            """).RootElement);

    [Fact, Trait("flow", "LEAVE")]
    public async Task LEAVE_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("LEAVE", "wilson@acme.test", JsonDocument.Parse("""
            {"leave_type":"事假","date_range":{"start":"2026-05-10","end":"2026-05-12"},"days":3,"reason":"reject"}
            """).RootElement);

    // ===== GEE — employee → manager → finance(confirm) → finance(review) =====

    [Fact, Trait("flow", "GEE")]
    public async Task GEE_happy_path_runs_to_completion()
        => await RunHappyAsync("GEE", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"business","expense_date":"2026-05-08","amount":1200,"currency":"TWD","invoice_no":"AA-12345678","description":"5/7 客戶午餐"}
            """).RootElement);

    [Fact, Trait("flow", "GEE")]
    public async Task GEE_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("GEE", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"misc","expense_date":"2026-05-08","amount":800,"currency":"TWD","description":"missing receipt"}
            """).RootElement);

    // ===== GEV — vendor expense, same shape as GEE =====

    [Fact, Trait("flow", "GEV")]
    public async Task GEV_happy_path_runs_to_completion()
        => await RunHappyAsync("GEV", "wilson@acme.test", JsonDocument.Parse("""
            {"vendor_name":"關貿網路股份有限公司","vendor_tax_id":"97162640","invoice_no":"AB-87654321","invoice_date":"2026-05-08","category":"outside_service","amount":33000,"currency":"TWD","vat_rate":"5","description":"GUI2.0 傳輸服務費用 May 2026","invoice_file":"invoice.pdf"}
            """).RootElement);

    [Fact, Trait("flow", "GEV")]
    public async Task GEV_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("GEV", "wilson@acme.test", JsonDocument.Parse("""
            {"vendor_name":"Test Vendor","invoice_no":"T-1","invoice_date":"2026-05-08","category":"other","amount":1000,"currency":"TWD","vat_rate":"5","description":"rejected case","invoice_file":"x.pdf"}
            """).RootElement);

    // ===== APE — advance expense =====

    [Fact, Trait("flow", "APE")]
    public async Task APE_happy_path_runs_to_completion()
        => await RunHappyAsync("APE", "wilson@acme.test", JsonDocument.Parse("""
            {"advance_amount":30000,"currency":"TWD","purpose":"5/20 客戶拜訪預估費用","expected_spend_date":"2026-05-20","expected_settle_date":"2026-06-15","charge_dept":"TWT.1746G - Elton Yang"}
            """).RootElement);

    [Fact, Trait("flow", "APE")]
    public async Task APE_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("APE", "wilson@acme.test", JsonDocument.Parse("""
            {"advance_amount":5000,"currency":"TWD","purpose":"理由不足","expected_spend_date":"2026-05-15","expected_settle_date":"2026-05-30","charge_dept":"TWT.1746G"}
            """).RootElement);

    // ===== HWP — hardware purchase, 7 steps =====

    [Fact, Trait("flow", "HWP")]
    public async Task HWP_happy_path_runs_to_completion()
        => await RunHappyAsync("HWP", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"laptop","item_name":"MacBook Air M3 13","spec":"13 inch / RAM 16GB / SSD 512GB","qty":1,"shipping_loc":"Taipei office","purpose":"new_hire","expected_date":"2026-06-15"}
            """).RootElement);

    [Fact, Trait("flow", "HWP")]
    public async Task HWP_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("HWP", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"monitor","item_name":"Dell 32 4K","spec":"32 inch 4K","qty":1,"shipping_loc":"Taipei","purpose":"additional"}
            """).RootElement);

    // ===== ITPR — IT software / SaaS purchase, same 7-step skeleton as HWP =====

    [Fact, Trait("flow", "ITPR")]
    public async Task ITPR_happy_path_runs_to_completion()
        => await RunHappyAsync("ITPR", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"saas","item_name":"Power BI Pro","vendor_name":"Microsoft","spec":"4 seats x 3 years","estimated_amount":26352,"currency":"TWD","purpose":"Finance dashboard 升級","expected_date":"2026-07-01"}
            """).RootElement);

    [Fact, Trait("flow", "ITPR")]
    public async Task ITPR_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("ITPR", "wilson@acme.test", JsonDocument.Parse("""
            {"category":"software","item_name":"Photoshop","vendor_name":"Adobe","spec":"1 seat","estimated_amount":800,"currency":"USD"}
            """).RootElement);

    // ===== TRQ — travel request, ends with admin (Pat) handling booking =====

    [Fact, Trait("flow", "TRQ")]
    public async Task TRQ_happy_path_runs_to_completion()
        => await RunHappyAsync("TRQ", "wilson@acme.test", JsonDocument.Parse("""
            {"destination":"Dubai, UAE","purpose":"DMCC 2026 RSM auditing","travel_dates":{"start":"2026-06-15","end":"2026-06-22"},"transport":"flight","estimated_amount":80000,"needs_pickup":"yes","pickup_address":"台北市大安區"}
            """).RootElement);

    [Fact, Trait("flow", "TRQ")]
    public async Task TRQ_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("TRQ", "wilson@acme.test", JsonDocument.Parse("""
            {"destination":"Tokyo","purpose":"non-essential","travel_dates":{"start":"2026-06-01","end":"2026-06-03"},"transport":"flight"}
            """).RootElement);

    // ===== TEO — travel expense reimbursement, threshold gateway =====
    // - under 50000 → manager → confirm → review (skips approval_extra)
    // - over 50000 → manager → approval_extra (any 2 of 3) → confirm → review

    [Fact, Trait("flow", "TEO")]
    public async Task TEO_happy_path_under_threshold_runs_to_completion()
        => await RunHappyAsync("TEO", "wilson@acme.test", JsonDocument.Parse("""
            {"trq_no":"","destination":"Taichung","purpose":"客戶拜訪","travel_dates":{"start":"2026-05-08","end":"2026-05-08"},"actual_amount":5000,"total_amount":5000,"expense_breakdown":"高鐵 1500\n計程車 1500\n餐費 2000","receipts":"receipts.pdf"}
            """).RootElement);

    [Fact, Trait("flow", "TEO")]
    public async Task TEO_happy_path_over_threshold_two_of_three_completes()
        => await RunHappyAsync("TEO", "wilson@acme.test", JsonDocument.Parse("""
            {"trq_no":"TW-TRQ-26-000160","destination":"Dubai, UAE","purpose":"DMCC 2026 RSM auditing","travel_dates":{"start":"2026-04-29","end":"2026-05-06"},"actual_amount":97000,"total_amount":97000,"expense_breakdown":"Airfare 50400\nHotel 44652\nTaxi 1948","receipts":"receipts.zip"}
            """).RootElement);

    [Fact, Trait("flow", "TEO")]
    public async Task TEO_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("TEO", "wilson@acme.test", JsonDocument.Parse("""
            {"destination":"Tokyo","purpose":"rejected case","travel_dates":{"start":"2026-05-01","end":"2026-05-02"},"actual_amount":12000,"total_amount":12000,"expense_breakdown":"x","receipts":"x.pdf"}
            """).RootElement);

    // ===== EXTOB — manager-initiated external onboarding (no approval node) =====
    // EXTOB has no approval, so there's no reject-path counterpart — the
    // submitter is yang (the hiring manager persona) instead of wilson.

    [Fact, Trait("flow", "EXTOB")]
    public async Task EXTOB_happy_path_runs_to_completion()
        => await RunHappyAsync("EXTOB", "yang@acme.test", JsonDocument.Parse("""
            {"first_name":"Raven","last_name":"Wang","business_title":"Cloud Platform Engineer","company":"廉誠資訊有限公司","onboard_date":"2026-06-01","company_email":"ext_ravenw@acme","department":"engineering","contract_no":"260517-AP-0001","contract_expiration":"2027-06-01","needs_mailbox":"yes"}
            """).RootElement);

    // ===== RESIGN — apply → manager → hr =====

    [Fact, Trait("flow", "RESIGN")]
    public async Task RESIGN_happy_path_runs_to_completion()
        => await RunHappyAsync("RESIGN", "wilson@acme.test", JsonDocument.Parse("""
            {"expected_last_day":"2026-06-30","reason_category":"career","reason_detail":"新公司 offer","handover_to":"Lin Tu","handover_items":"ProjectX 後端 API、CI pipelines"}
            """).RootElement);

    [Fact, Trait("flow", "RESIGN")]
    public async Task RESIGN_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("RESIGN", "wilson@acme.test", JsonDocument.Parse("""
            {"expected_last_day":"2026-05-25","reason_category":"other","handover_to":"Lin Tu"}
            """).RootElement);

    // ===== DEPTX — apply → manager → hr =====

    [Fact, Trait("flow", "DEPTX")]
    public async Task DEPTX_happy_path_runs_to_completion()
        => await RunHappyAsync("DEPTX", "wilson@acme.test", JsonDocument.Parse("""
            {"current_department":"Engineering","target_department":"it","effective_date":"2026-07-01","reason":"業務重組，配合 Platform team 集中管理","salary_impact":"no","new_manager":"Lin Tu"}
            """).RootElement);

    [Fact, Trait("flow", "DEPTX")]
    public async Task DEPTX_reject_path_terminates_with_errored_status()
        => await RunRejectAsync("DEPTX", "wilson@acme.test", JsonDocument.Parse("""
            {"current_department":"Engineering","target_department":"finance","effective_date":"2026-06-01","reason":"rejected case","salary_impact":"tbd"}
            """).RootElement);

    // ===== Shared helpers =====

    /// <summary>
    /// Standard happy-path harness: enable sandbox, start an instance as the
    /// named persona, drive every open task to completion using
    /// <see cref="IProcessRuntime.SubmitTaskAsAdminAsync"/> (bypasses the
    /// "actor must be assignee" gate so we don't need persona JWTs in this
    /// harness), then assert the four contract points listed in PR-L5 §5.
    /// </summary>
    private async Task RunHappyAsync(string specCode, string submitterEmail, JsonElement formData)
    {
        await EnableSandboxAsync();

        var submitterId = _userByEmail[submitterEmail];
        var runtime = BuildRuntime();
        var start = await runtime.StartInstanceAsync(
            new StartInstanceCommand(specCode, formData, submitterId));

        await DriveToCompletionAsync(runtime, start.InstanceId, expectReject: false);

        await using var verify = NewCtx();
        var instance = await verify.ProcessInstances.AsNoTracking()
            .SingleAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instance.Status);

        // (a) TaskHistory contains InstanceCompleted.
        Assert.True(await verify.TaskHistory
            .AnyAsync(h => h.ProcessInstanceId == start.InstanceId
                           && h.EventType == HistoryEventType.InstanceCompleted),
            $"{specCode}: expected InstanceCompleted history row");

        // (b) Sandbox capture fired at least once — the on_complete (or
        // on_assign) notification went through the OutboundGate. Some specs
        // (EXTOB) have only on_assign + on_complete; one captured row proves
        // the dispatch pipeline was wired up correctly.
        var captureCount = await verify.SandboxCapturedMessages
            .CountAsync(m => m.ProcessInstanceId == start.InstanceId);
        Assert.True(captureCount > 0,
            $"{specCode}: expected at least one SandboxCapturedMessages row, got {captureCount}");

        // (c) Mine-instance projection sees the completed case.
        var queries = BuildQueryService();
        var mine = await queries.GetMyInstancesAsync(submitterId, status: "completed", limit: 50);
        Assert.Contains(mine, m => m.Id == start.InstanceId);
    }

    /// <summary>
    /// Standard reject-path harness: drive any leading userTasks (where the
    /// initiator submits the form) until we reach the first Approval node,
    /// then submit Reject. Per design.md §12 the runtime sets
    /// <see cref="InstanceStatus.Errored"/> and writes
    /// <see cref="HistoryEventType.ApprovalRejected"/>.
    /// </summary>
    private async Task RunRejectAsync(string specCode, string submitterEmail, JsonElement formData)
    {
        await EnableSandboxAsync();

        var submitterId = _userByEmail[submitterEmail];
        var runtime = BuildRuntime();
        var start = await runtime.StartInstanceAsync(
            new StartInstanceCommand(specCode, formData, submitterId));

        await DriveToCompletionAsync(runtime, start.InstanceId, expectReject: true);

        await using var verify = NewCtx();
        var instance = await verify.ProcessInstances.AsNoTracking()
            .SingleAsync(i => i.Id == start.InstanceId);
        Assert.Equal(InstanceStatus.Errored, instance.Status);

        Assert.True(await verify.TaskHistory
            .AnyAsync(h => h.ProcessInstanceId == start.InstanceId
                           && h.EventType == HistoryEventType.ApprovalRejected),
            $"{specCode}: expected ApprovalRejected history row");
    }

    /// <summary>
    /// Drive the next open task forward at most 64 times. For approvals,
    /// submit Approve in happy mode and Reject (once) in reject mode. For
    /// userTasks no decision is needed.
    /// </summary>
    private async Task DriveToCompletionAsync(IProcessRuntime runtime, Guid instanceId, bool expectReject)
    {
        var rejectedAlready = false;
        for (var i = 0; i < 64; i++)
        {
            ProcessTask? next;
            InstanceStatus status;
            await using (var db = NewCtx())
            {
                var inst = await db.ProcessInstances.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == instanceId);
                if (inst is null) return;
                status = inst.Status;
                if (status != InstanceStatus.Running) return;

                next = await db.ProcessTasks.AsNoTracking()
                    .Where(t => t.ProcessInstanceId == instanceId
                                && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                    .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                    .FirstOrDefaultAsync();
            }
            if (next is null)
                throw new InvalidOperationException(
                    $"instance {instanceId} is Running but has no open task — stuck mid-flow");

            var actor = next.ActualAssigneeUserId ?? next.OriginalAssigneeUserId
                ?? throw new InvalidOperationException(
                    $"task {next.Id} has neither ActualAssigneeUserId nor OriginalAssigneeUserId");

            Decision? decision = null;
            if (next.NodeKind == NodeKind.Approval)
            {
                if (expectReject && !rejectedAlready)
                {
                    decision = Decision.Reject;
                    rejectedAlready = true;
                }
                else
                {
                    decision = Decision.Approve;
                }
            }

            // SubmitTaskAsAdminAsync bypasses the assignee gate. The runtime
            // still records ActualAssigneeUserId on the task row, so audit /
            // notification context stays correct — only the auth check moves.
            await runtime.SubmitTaskAsAdminAsync(new SubmitTaskCommand(
                next.Id, actor, FormDataPatch: null, Decision: decision, Comment: "auto-driven by AllFlowsRealE2ETests"));
        }
        throw new InvalidOperationException(
            $"instance {instanceId} did not terminate within 64 steps");
    }

    /// <summary>Toggle SandboxMode on so the capturing dispatcher writes rows.</summary>
    private async Task EnableSandboxAsync()
    {
        var sandbox = new SandboxService(NewCtx(), NullLogger<SandboxService>.Instance);
        await sandbox.SetStatusAsync(
            new UpdateSandboxRequest(Enabled: true, Config: null),
            actorUserId: _userByEmail["jason_test@acme.test"]);
    }

    private AppDbContext NewCtx() => new(_options);

    /// <summary>
    /// Build a real ProcessRuntime wired to:
    /// - <see cref="FileSystemSpecLoader"/> reading sample_specs/
    /// - <see cref="ActorResolver"/> backed by the persona-seeded org
    /// - <see cref="SandboxCapturingNotificationDispatcher"/> so on_assign /
    ///   on_complete writes land in SandboxCapturedMessages
    /// </summary>
    private ProcessRuntime BuildRuntime()
    {
        var runtimeDb = NewCtx();
        var orgReader = new OrgChartReader(runtimeDb);
        var resolver = new ActorResolver(orgReader, new NoOpResolutionAuditor(),
            NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var gate = new OutboundGate(NewCtx(), _clock);
        var dispatcher = new SandboxCapturingNotificationDispatcher(
            gate, NewCtx(), NullLogger<SandboxCapturingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bpm:SampleSpecsDir"] = "/Users/jason/claude/bpm/sample_specs",
            })
            .Build();
        var specLoader = new FileSystemSpecLoader(cfg, NullLogger<FileSystemSpecLoader>.Instance);
        return new ProcessRuntime(runtimeDb, _clock, specLoader, resolver, delegation, dispatcher,
            evaluator, NullLogger<ProcessRuntime>.Instance);
    }

    /// <summary>
    /// Build the same <see cref="ProcessQueryService"/> the
    /// <c>GET /api/processes/mine</c> endpoint uses, so the assertion exercises
    /// the production query path rather than a hand-written EF query.
    /// </summary>
    private ProcessQueryService BuildQueryService()
    {
        var db = NewCtx();
        return new ProcessQueryService(db, _clock);
    }
}

using System.Text.Json;
using Bpm.Api.Auth;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
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
using Bpm.Persistence.Spec.Bundle;
using Bpm.Tests.Application.Spec.Bundle;
using Bpm.Tests.Common;
using Bpm.Tests.Persistence.Spec.Bundle;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Tests.Integration;

/// <summary>
/// PR-J6 §14.1 — single end-to-end acceptance loop exercising the full
/// "无痛验收" (selling point 2) demo path through service calls only:
/// toggle sandbox on, install LEAVE bundle, submit as employee, advance
/// clock, inspect mailbox, switch persona, approve as manager, inspect
/// mailbox again, reset, re-run identically.
///
/// <para>
/// The org-snapshot from <see cref="BundleTestFactory.DefaultOrg"/> uses
/// Alice (manager) and Bob (employee, reports to Alice) — they stand in
/// for the prompt's Mary / Wilson roles. The bundle scratch tenant the
/// runtime loader seeds is what the runtime queries, so submit / approve
/// happen against those user ids.
/// </para>
///
/// <para>
/// Marked <c>[Trait("category", "integration")]</c> so the future CI
/// pipeline can split fast unit tests from this slower scenario suite —
/// mirrors the convention <see cref="BundleE2ETests"/> established.
/// </para>
/// </summary>
[Trait("category", "integration")]
public sealed class SandboxAcceptanceLoopTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public SandboxAcceptanceLoopTests()
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
    public async Task Sandbox_acceptance_loop_full_demo()
    {
        // Step 0 — install the LEAVE bundle and surface the canonical user ids.
        var bundleBytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var parsed = await new BundleParser(NullLogger<BundleParser>.Instance).ParseAsync(new MemoryStream(bundleBytes));
        var loader = new BundleRuntimeLoader(NewCtx(), NullLogger<BundleRuntimeLoader>.Instance);
        await using var handle = await loader.LoadAsync(parsed);

        var bobId = handle.InitiatorUserId;                  // employee — Bob (reports to Alice)
        var aliceId = await ResolveAliceIdAsync(bobId);      // manager  — Alice
        Assert.NotEqual(Guid.Empty, aliceId);

        // Phase A — first pass through the loop.
        var inputs = JsonDocument.Parse("{\"leave_type\":\"特休\"}").RootElement.Clone();
        var firstInstanceId = await DriveOneLoopAsync(handle, inputs, bobId, aliceId);

        // Step 8/9 — reset the instance; rows for it must be gone.
        await ResetAndAssertEmptyAsync(firstInstanceId);

        // Step 10 — re-run identically. Same handle (scratch tenant survives the
        // reset because reset is scoped to the default tenant's runtime rows,
        // not the loader's scratch tenant), same inputs, fresh instance id.
        // Proves the loop is deterministic / repeatable.
        var secondInstanceId = await DriveOneLoopAsync(handle, inputs, bobId, aliceId);
        Assert.NotEqual(firstInstanceId, secondInstanceId);
    }

    /// <summary>
    /// Steps 1–7 of the demo loop. Returns the instance id so the caller
    /// can drive the reset assertion (step 8/9) and then invoke a second pass.
    /// </summary>
    private async Task<Guid> DriveOneLoopAsync(
        LoadedBundleHandle handle, JsonElement inputs, Guid bobId, Guid aliceId)
    {
        // Step 1 — toggle sandbox ON.
        var sandboxService = new SandboxService(NewCtx(), NullLogger<SandboxService>.Instance);
        var statusOn = await sandboxService.SetStatusAsync(
            new UpdateSandboxRequest(Enabled: true, Config: null), actorUserId: aliceId);
        Assert.True(statusOn.Enabled);

        // Step 2 — submit LEAVE instance via IProcessRuntime as Bob (employee).
        var runtime = BuildRuntime();
        var startCmd = new StartInstanceCommand(handle.RegisteredSpecCode, inputs, bobId);
        var start = await runtime.StartInstanceAsync(startCmd, handle.SpecJson);
        var instanceId = start.InstanceId;

        // Step 3 — advance clock 48 hours via ISandboxClockService. Capture
        // the pre-advance offset because the per-instance reset preserves it
        // (only ResetAll touches the clock); the second pass through the loop
        // sees the accumulated 48h on top of what was already there.
        var clockService = NewClockService();
        var preAdvance = await clockService.GetAsync();
        var advanced = await clockService.AdvanceAsync(days: 2, hours: 0, minutes: 0, seconds: 0);
        Assert.True(advanced.SandboxOn);
        Assert.Equal(preAdvance.OffsetSeconds + 2L * 86400L, advanced.OffsetSeconds);

        // Step 4 — read mailbox: at least one captured email for this instance.
        // The on_assign trigger fires when the manager-approval task spawns, and
        // SandboxCapturingNotificationDispatcher writes a row through OutboundGate.
        await using (var verify = NewCtx())
        {
            var emails = await verify.SandboxCapturedMessages
                .AsNoTracking()
                .Where(m => m.ProcessInstanceId == instanceId
                            && m.Channel == SandboxChannel.Email)
                .ToListAsync();
            Assert.NotEmpty(emails);
            // The on_assign template's subject contains "請假待簽" — proves the
            // capture pipeline rendered subject + recorded the originating id.
            Assert.Contains(emails, e => (e.Subject ?? "").Contains("請假待簽"));
            Assert.Contains(emails, e => e.OriginatingNotificationId == "notify_assign_manager");
        }

        // Step 5 — switch persona to Alice (manager). The token issuance is the
        // sandbox API contract; we don't attempt to round-trip through the JWT
        // middleware (no HTTP host in this fixture), but we DO assert the token
        // carries the expected actual_actor / sandbox_actor claims so the
        // contract is locked in.
        var jwt = new JwtTokenService(
            new JwtOptions { Secret = new string('x', 64), Issuer = "bpm-svc-test", Audience = "bpm-test" },
            new TestEnv(),
            NullLogger<JwtTokenService>.Instance);
        var (token, expiresAt) = jwt.IssueSandboxPersonaToken(
            personaUserId: aliceId,
            personaEmail: "alice@acme.tld",
            personaFullName: "Alice",
            personaRoles: new[] { "manager" },
            actualActorUserId: bobId,
            actualActorEmail: "bob@acme.tld");
        Assert.NotEmpty(token);
        Assert.True(expiresAt > DateTime.UtcNow);

        // Step 6 — approve the open task as Alice. We pull the task's actual
        // assignee + ApproveDecision through the runtime, no UI required.
        await DriveLoopAsync(runtime, instanceId, fallbackActor: aliceId);

        // Step 7 — read mailbox again: assert the on_complete (notify_complete)
        // email landed too; the LEAVE spec sends "您的請假已備案" on completion.
        await using (var verify = NewCtx())
        {
            var emails = await verify.SandboxCapturedMessages
                .AsNoTracking()
                .Where(m => m.ProcessInstanceId == instanceId
                            && m.Channel == SandboxChannel.Email)
                .ToListAsync();
            Assert.Contains(emails, e => (e.Subject ?? "").Contains("您的請假已備案"));
            Assert.Contains(emails, e => e.OriginatingNotificationId == "notify_complete");
        }

        // The instance reached Completed — proves the persona-mediated approval
        // walked the runtime end to end, no HTTP middleware required.
        await using (var verifyDb = NewCtx())
        {
            var inst = await verifyDb.ProcessInstances.AsNoTracking()
                .SingleAsync(i => i.Id == instanceId);
            Assert.Equal(InstanceStatus.Completed, inst.Status);
        }

        return instanceId;
    }

    private async Task ResetAndAssertEmptyAsync(Guid instanceId)
    {
        // Step 8 — reset the instance. ResetService bypasses the append-only
        // TaskHistory guard via ExecuteDeleteAsync.
        var clockService = NewClockService();
        var resetService = new ResetService(NewCtx(), clockService, NullLogger<ResetService>.Instance);
        var summary = await resetService.ResetInstanceAsync(instanceId, actorUserId: Guid.NewGuid());
        Assert.Equal(1, summary.InstancesDeleted);
        Assert.True(summary.HistoryRowsDeleted > 0, "expected history rows to be deleted");

        // Step 9 — assert the instance leaves no rows behind across every table
        // the runtime touched (instance / tasks / history / captured).
        await using var verify = NewCtx();
        Assert.Equal(0, await verify.ProcessInstances.CountAsync(i => i.Id == instanceId));
        Assert.Equal(0, await verify.ProcessTasks.CountAsync(t => t.ProcessInstanceId == instanceId));
        Assert.Equal(0, await verify.TaskHistory.CountAsync(h => h.ProcessInstanceId == instanceId));
        Assert.Equal(0, await verify.SandboxCapturedMessages.CountAsync(m => m.ProcessInstanceId == instanceId));
    }

    // ===== harness =====

    private AppDbContext NewCtx() => new(_options);

    private SandboxClockService NewClockService()
    {
        var db = NewCtx();
        var sysClock = new SystemClock();
        var sandboxClock = new SandboxClock(NewCtx(), sysClock);
        return new SandboxClockService(db, sysClock, sandboxClock,
            new NoOpScheduledJobKicker(), NullLogger<SandboxClockService>.Instance);
    }

    private ProcessRuntime BuildRuntime()
    {
        // Distinct AppDbContext per collaborator so the change-tracker doesn't
        // see the same entity from multiple consumers — mirrors the wiring
        // BundleE2ETests + BundleReproWithNotificationsTests use.
        var runtimeDb = NewCtx();
        var orgReader = new OrgChartReader(runtimeDb);
        var resolver = new ActorResolver(orgReader, new NoOpResolutionAuditor(),
            NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var gate = new OutboundGate(NewCtx(), _clock);
        var dispatcher = new SandboxCapturingNotificationDispatcher(
            gate, NewCtx(), NullLogger<SandboxCapturingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var specLoader = new NullSpecLoader();
        return new ProcessRuntime(runtimeDb, _clock, specLoader, resolver, delegation, dispatcher,
            evaluator, NullLogger<ProcessRuntime>.Instance);
    }

    /// <summary>
    /// Finds Bob's manager (Alice) without hardcoding the seed Guid — the
    /// scratch tenant's user ids match the bundle snapshot, so the lookup is
    /// trivial. Decoupled from the test body so future fixtures can drop in
    /// an alternative org without rewriting the demo loop.
    /// </summary>
    private async Task<Guid> ResolveAliceIdAsync(Guid bobId)
    {
        await using var db = NewCtx();
        var bob = await db.Users.AsNoTracking().SingleAsync(u => u.Id == bobId);
        return bob.ManagerId ?? Guid.Empty;
    }

    /// <summary>
    /// Pulls every open task and submits with Approve where the node is an
    /// Approval, no decision otherwise. The sandbox-capturing dispatcher fires
    /// during each submit so on_assign / on_complete notifications land in the
    /// captured-messages table.
    /// </summary>
    private async Task DriveLoopAsync(IProcessRuntime runtime, Guid instanceId, Guid fallbackActor)
    {
        for (var i = 0; i < 64; i++)
        {
            ProcessTask? next;
            await using (var db = NewCtx())
            {
                var instance = await db.ProcessInstances.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == instanceId);
                if (instance is null || instance.Status != InstanceStatus.Running) return;
                next = await db.ProcessTasks.AsNoTracking()
                    .Where(t => t.ProcessInstanceId == instanceId
                                && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                    .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                    .FirstOrDefaultAsync();
            }
            if (next is null) return;

            var actor = next.ActualAssigneeUserId ?? next.OriginalAssigneeUserId ?? fallbackActor;
            Decision? decision = next.NodeKind == NodeKind.Approval ? Decision.Approve : null;
            await runtime.SubmitTaskAsync(new SubmitTaskCommand(next.Id, actor,
                FormDataPatch: null, Decision: decision, Comment: null));
        }
    }

    private sealed class TestEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

using System.Text.Json;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Sandbox;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Notifications;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Sandbox;
using Bpm.Persistence.Spec.Bundle;
using Bpm.Tests.Application.Spec.Bundle;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Persistence.Spec.Bundle;

/// <summary>
/// PR-J6 §11.6 — repro runner asserts captured notifications.
///
/// <para>
/// Wires a real <see cref="SandboxCapturingNotificationDispatcher"/> +
/// <see cref="OutboundGate"/> behind the runtime so notifications fired by
/// the LEAVE flow land in <c>SandboxCapturedMessages</c> for the assertion
/// pass to find. The runner itself flips sandbox on/off for the duration of
/// the run (see PR-J6 §11.2) so the per-test setup doesn't have to.
/// </para>
/// </summary>
public sealed class BundleReproWithNotificationsTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public BundleReproWithNotificationsTests()
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
    public async Task Repro_runner_asserts_captured_notifications()
    {
        // LEAVE bundle's notifications:
        //   notify_assign_manager — subject "【請假待簽】..." on_assign
        //   notify_complete       — subject "您的請假已備案"   on_complete
        // Both should fire during the happy-path run; we assert on the manager
        // subject substring.
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        var withExpect = new TestCaseSnapshot(
            Id: "tc_with_notifications",
            Name: "expects manager notification",
            Inputs: doc.RootElement.Clone(),
            ExpectedTrace: new[]
            {
                "start_1", "task_apply", "approval_manager",
                "gateway_days", "task_hr_archive", "end_1",
            },
            ExpectedNotifications: new[]
            {
                new ExpectedNotification(SubjectContains: "請假"),
            });

        var (handle, runner, cases) = await BuildHarnessAsync(new[] { withExpect });
        await using var _h = handle;

        var report = await runner.RunAsync(handle, cases);

        Assert.Equal(ReproStatus.Pass, report.OverallStatus);
        var caseResult = Assert.Single(report.Cases);
        Assert.Equal(ReproStatus.Pass, caseResult.Status);
        Assert.Null(caseResult.Diff);
        Assert.NotNull(caseResult.NotificationAssertions);
        var notif = Assert.Single(caseResult.NotificationAssertions!);
        Assert.True(notif.Passed, $"notification assertion should pass; diff={notif.Diff}");
    }

    [Fact]
    public async Task Repro_runner_fails_when_notification_missing()
    {
        // The expected substring won't appear in any LEAVE notification subject,
        // so the assertion must fail — and the case overall must Fail even though
        // the node trace itself matches.
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        var withBogusExpect = new TestCaseSnapshot(
            Id: "tc_with_bogus_notification",
            Name: "expects nonexistent subject",
            Inputs: doc.RootElement.Clone(),
            ExpectedTrace: new[]
            {
                "start_1", "task_apply", "approval_manager",
                "gateway_days", "task_hr_archive", "end_1",
            },
            ExpectedNotifications: new[]
            {
                new ExpectedNotification(SubjectContains: "永遠不會出現的字串_zz_xyzzy"),
            });

        var (handle, runner, cases) = await BuildHarnessAsync(new[] { withBogusExpect });
        await using var _h = handle;

        var report = await runner.RunAsync(handle, cases);

        Assert.Equal(ReproStatus.Fail, report.OverallStatus);
        var caseResult = Assert.Single(report.Cases);
        Assert.Equal(ReproStatus.Fail, caseResult.Status);
        // Trace itself was clean — the failure is on the assertion side only.
        Assert.Null(caseResult.Diff);
        var notif = Assert.Single(caseResult.NotificationAssertions!);
        Assert.False(notif.Passed);
        Assert.NotNull(notif.Diff);
        Assert.Contains("no captured email matched", notif.Diff!);
    }

    [Fact]
    public async Task Repro_runner_restores_prior_sandbox_state_after_run()
    {
        // Pre-seed sandbox-off + a non-zero clock offset; runner must flip on
        // for the duration and restore the original state on exit.
        await using (var seed = new AppDbContext(_options))
        {
            seed.TenantSettings.Add(new Bpm.Domain.Entities.Sandbox.TenantSettings
            {
                TenantCode = "default",
                SandboxMode = false,
                SandboxClockOffsetSeconds = 1234L,
            });
            await seed.SaveChangesAsync();
        }

        var (handle, runner, cases) = await BuildHarnessAsync(BundleTestFactory.DefaultCases());
        await using var _h = handle;

        var report = await runner.RunAsync(handle, cases);
        Assert.Equal(ReproStatus.Pass, report.OverallStatus);

        await using var verify = new AppDbContext(_options);
        var settings = await verify.TenantSettings.AsNoTracking()
            .SingleAsync(s => s.TenantCode == "default");
        Assert.False(settings.SandboxMode);
        Assert.Equal(1234L, settings.SandboxClockOffsetSeconds);
    }

    private async Task<(LoadedBundleHandle Handle, BundleReproducibilityRunner Runner, IReadOnlyList<TestCaseSnapshot> Cases)>
        BuildHarnessAsync(IReadOnlyList<TestCaseSnapshot> cases)
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync(cases: cases);
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        using var ms = new MemoryStream(bytes);
        var parsed = await parser.ParseAsync(ms);

        var loaderDb = new AppDbContext(_options);
        var loader = new BundleRuntimeLoader(loaderDb, NullLogger<BundleRuntimeLoader>.Instance);
        var handle = await loader.LoadAsync(parsed);

        // Real notification dispatcher backed by the real OutboundGate so
        // captures actually land in SandboxCapturedMessages when the runner
        // flips sandbox on. Each collaborator gets its own AppDbContext so EF's
        // change-tracker doesn't trip over shared entities.
        var runtimeDb = new AppDbContext(_options);
        var orgReader = new OrgChartReader(runtimeDb);
        var auditor = new NoOpResolutionAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var gateDb = new AppDbContext(_options);
        var gate = new OutboundGate(gateDb, _clock);
        var dispatcherDb = new AppDbContext(_options);
        var dispatcher = new SandboxCapturingNotificationDispatcher(
            gate, dispatcherDb, NullLogger<SandboxCapturingNotificationDispatcher>.Instance);
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var loaderStub = new NullSpecLoader();
        var runtime = new ProcessRuntime(runtimeDb, _clock, loaderStub, resolver, delegation, dispatcher,
            evaluator, NullLogger<ProcessRuntime>.Instance);

        var runnerDb = new AppDbContext(_options);
        var runner = new BundleReproducibilityRunner(runnerDb, runtime,
            NullLogger<BundleReproducibilityRunner>.Instance);

        return (handle, runner, cases);
    }
}

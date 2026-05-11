using System.Text.Json;
using Bpm.Application.Delegation;
using Bpm.Application.Org;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Spec;
using Bpm.Persistence.Spec.Bundle;
using Bpm.Tests.Application.Spec.Bundle;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Persistence.Spec.Bundle;

/// <summary>
/// PR-I4 §5.5-5.9 — repro runner end-to-end. Builds the LEAVE bundle
/// via <see cref="BundleTestFactory"/>, loads it under a scratch tenant,
/// and drives the live runtime against the bundled test cases.
/// </summary>
public sealed class BundleReproducibilityRunnerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public BundleReproducibilityRunnerTests()
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

    // 5.8 — happy path: bundled LEAVE test case replays cleanly.
    [Fact]
    public async Task Happy_path_returns_overall_pass()
    {
        var (handle, runner, cases) = await BuildHarnessAsync(BundleTestFactory.DefaultCases());
        await using var _h = handle;

        var report = await runner.RunAsync(handle, cases);

        Assert.True(report.OverallStatus == ReproStatus.Pass,
            $"Expected Pass but got Fail; diff={report.Cases.FirstOrDefault()?.Diff}");
        Assert.Single(report.Cases);
        var caseResult = report.Cases[0];
        Assert.Equal(ReproStatus.Pass, caseResult.Status);
        Assert.Null(caseResult.Diff);
        Assert.Equal(caseResult.ExpectedTrace, caseResult.ActualTrace);
    }

    // 5.9 — tampered expectedTrace surfaces a clean Fail with a diff
    // that names the spurious node.
    [Fact]
    public async Task Tampered_expected_trace_returns_fail_with_diff()
    {
        // Insert a fake node id between the gateway and the HR archive.
        // The actual run won't visit "phantom_node", so the diff must
        // cite it.
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        var tampered = new TestCaseSnapshot(
            Id: "tc_tampered",
            Name: "tampered with phantom node",
            Inputs: doc.RootElement.Clone(),
            ExpectedTrace: new[]
            {
                "start_1", "task_apply", "approval_manager",
                "gateway_days", "phantom_node", "task_hr_archive", "end_1",
            });

        var (handle, runner, cases) = await BuildHarnessAsync(new[] { tampered });
        await using var _h = handle;

        var report = await runner.RunAsync(handle, cases);

        Assert.Equal(ReproStatus.Fail, report.OverallStatus);
        var caseResult = Assert.Single(report.Cases);
        Assert.Equal(ReproStatus.Fail, caseResult.Status);
        Assert.NotNull(caseResult.Diff);
        Assert.Contains("phantom_node", caseResult.Diff!);
        Assert.DoesNotContain("phantom_node", caseResult.ActualTrace);
    }

    // ===== harness =====

    private async Task<(LoadedBundleHandle Handle, BundleReproducibilityRunner Runner, IReadOnlyList<TestCaseSnapshot> Cases)>
        BuildHarnessAsync(IReadOnlyList<TestCaseSnapshot> cases)
    {
        // Build the LEAVE bundle via the shared factory and parse it back —
        // mirrors the production wire (zip on disk → parser → loader).
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync(cases: cases);
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        using var ms = new MemoryStream(bytes);
        var parsed = await parser.ParseAsync(ms);

        var db = new AppDbContext(_options);
        var loader = new BundleRuntimeLoader(db, NullLogger<BundleRuntimeLoader>.Instance);
        var handle = await loader.LoadAsync(parsed);

        // Build a runtime against the same DbContext options. The repro
        // runner gets the inline-spec overload, so ISpecLoader is never
        // consulted — pass a dummy that always returns null to make the
        // contract explicit.
        var runtimeDb = new AppDbContext(_options);
        var orgReader = new OrgChartReader(runtimeDb);
        var auditor = new NoOpResolutionAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var notifier = new StubNotificationDispatcher();
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var loaderStub = new NullSpecLoader();
        var runtime = new ProcessRuntime(runtimeDb, _clock, loaderStub, resolver, delegation, notifier,
            evaluator, NullLogger<ProcessRuntime>.Instance);

        var runnerDb = new AppDbContext(_options);
        var runner = new BundleReproducibilityRunner(runnerDb, runtime,
            NullLogger<BundleReproducibilityRunner>.Instance);

        return (handle, runner, cases);
    }
}

internal sealed class NoOpResolutionAuditor : IActorResolutionAuditor
{
    public Task WriteAsync(Bpm.Domain.Spec.ActorRef actor, Bpm.Domain.Spec.ResolutionContext ctx,
        Bpm.Domain.Spec.ResolutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class NullSpecLoader : ISpecLoader
{
    public Task<string?> LoadAsync(string tenantCode, string specCode, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class StubNotificationDispatcher : Bpm.Application.Notifications.INotificationDispatcher
{
    public Task DispatchAsync(Bpm.Application.Process.Runtime.SpecSnapshot snapshot, string trigger,
        Bpm.Application.Notifications.NotificationContext ctx, CancellationToken ct = default)
        => Task.CompletedTask;
}

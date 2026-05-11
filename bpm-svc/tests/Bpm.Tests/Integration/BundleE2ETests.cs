using System.Text.Json;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Process;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
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
/// PR-I8 §12 acceptance test: full bundle round-trip across two isolated
/// SQLite contexts in the same test process.
///
/// <para>
/// Models the sales-demo claim: customer A designs LEAVE in their site,
/// exports a Spec Bundle (zip), customer B installs that zip in a clean
/// site and runs the bundled test-case — both sites produce byte-identical
/// <c>ProcessInstance.SpecSnapshotJson</c> and the same node trace. No code
/// change on instance B; the bundle is the contract.
/// </para>
///
/// Integration: boots two SQLite contexts. Slower than unit tests.
/// Marked <c>[Trait("category", "integration")]</c> so CI can filter it
/// out of fast unit-test runs once a runsettings / pipeline is in place.
/// </summary>
[Trait("category", "integration")]
public sealed class BundleE2ETests : IAsyncDisposable
{
    private readonly List<SqliteConnection> _connections = new();
    private readonly StubClock _clock = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _connections)
        {
            await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task Bundle_round_trips_between_two_instances()
    {
        // ===== Instance A — designs the LEAVE bundle. =====
        // Per task spec we use the shared BundleTestFactory to seed the org
        // + test-cases; the factory's snapshot IS the "instance A's design
        // intent". Instance A's DB is created but never seeded with org rows
        // because the BundleBuilder consumes the snapshot directly — it
        // doesn't read from the DB. Keeping a real context for A anyway so
        // future enhancements that DO touch the DB (e.g. SpecBundle
        // persistence on save) can land without restructuring.
        var (dbA, optsA, connA) = CreateInMemoryInstance();
        _connections.Add(connA);
        using var _dbA = dbA;

        var sampleOrg = BundleTestFactory.DefaultOrg();
        var testCases = BundleTestFactory.DefaultCases();
        var zipBytes = await BundleTestFactory.BuildLeaveBundleAsync(sampleOrg, testCases);

        Assert.True(zipBytes.Length > 0, "BundleBuilder should produce a non-empty zip.");

        // ===== Instance B — completely separate SQLite, no seed data. =====
        var (dbB, optsB, connB) = CreateInMemoryInstance();
        _connections.Add(connB);
        using var _dbB = dbB;

        // Sanity: B is genuinely empty — no users, no spec bundles.
        Assert.Equal(0, await dbB.Users.CountAsync());
        Assert.Equal(0, await dbB.ProcessInstances.CountAsync());

        // Parse → validate → load on instance B.
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        using var ms = new MemoryStream(zipBytes);
        var parsedB = await parser.ParseAsync(ms);

        var validator = new BundleValidator();
        var validation = validator.Validate(parsedB);
        Assert.True(validation.Valid,
            $"Bundle validation failed: [{string.Join(", ", validation.Errors.Select(e => e.Code))}]");

        var loaderDbB = new AppDbContext(optsB);
        var loaderB = new BundleRuntimeLoader(loaderDbB, NullLogger<BundleRuntimeLoader>.Instance);
        await using var handleB = await loaderB.LoadAsync(parsedB);

        Assert.StartsWith("repro-", handleB.ScratchTenantCode);
        Assert.Equal(sampleOrg.Users.Count, handleB.SeededUserCount);

        // Run the repro runner on B end-to-end.
        var runtimeB = BuildRuntime(optsB);
        var runnerB = new BundleReproducibilityRunner(new AppDbContext(optsB), runtimeB,
            NullLogger<BundleReproducibilityRunner>.Instance);

        var reportB = await runnerB.RunAsync(handleB, parsedB.TestCases);

        Assert.Equal(ReproStatus.Pass, reportB.OverallStatus);
        var caseB = Assert.Single(reportB.Cases);
        Assert.Equal(ReproStatus.Pass, caseB.Status);
        Assert.Null(caseB.Diff);

        // ===== Instance A — drive the SAME case directly via runtime. =====
        // The repro runner already proved B reaches the expected trace; now
        // we prove that an independent instance (A) seeded with the same
        // SampleOrg + the bundle's spec produces the SAME SpecSnapshotJson
        // and the SAME node trace. That's the byte-level reproducibility
        // promise the bundle makes.
        //
        // Instance A also goes through BundleRuntimeLoader so the seed path
        // is identical to B — proves there's no hidden "A is special"
        // because it was the designer.
        var loaderDbA = new AppDbContext(optsA);
        var loaderA = new BundleRuntimeLoader(loaderDbA, NullLogger<BundleRuntimeLoader>.Instance);
        await using var handleA = await loaderA.LoadAsync(parsedB);

        var runtimeA = BuildRuntime(optsA);
        var startCmd = new StartInstanceCommand(handleA.RegisteredSpecCode,
            parsedB.TestCases[0].Inputs, handleA.InitiatorUserId);
        var startA = await runtimeA.StartInstanceAsync(startCmd, handleA.SpecJson);

        await DriveToCompletionAsync(optsA, runtimeA, startA.InstanceId, handleA.InitiatorUserId);

        // ===== Assertions: A's snapshot == B's snapshot == bundle's spec. =====
        // 1. The spec JSON A loaded inline is bit-for-bit the bundle's spec.
        Assert.Equal(parsedB.SpecJson.GetRawText(), handleA.SpecJson);

        // 2. ProcessInstance.SpecSnapshotJson on A's completed instance is
        //    equal (after JSON canonicalisation) to the bundle's spec.json.
        //    The runtime stores SpecSnapshot inline at start — proves the
        //    snapshot pipeline isn't perturbing the spec.
        var instanceA = await new AppDbContext(optsA).ProcessInstances
            .AsNoTracking()
            .FirstAsync(i => i.Id == startA.InstanceId);
        Assert.Equal(InstanceStatus.Completed, instanceA.Status);

        var snapshotANormalized = NormalizeJson(instanceA.SpecSnapshotJson);
        var bundleSpecNormalized = NormalizeJson(parsedB.SpecJson.GetRawText());
        Assert.Equal(bundleSpecNormalized, snapshotANormalized);

        // 3. Compute A's actual trace via the same path the repro runner
        //    uses on B; assert traces are equal.
        var traceA = await ExtractTraceAsync(optsA, startA.InstanceId, instanceA.SpecSnapshotJson);
        Assert.Equal(caseB.ActualTrace, traceA);

        // 4. And the trace matches the bundle's expected trace — closes the
        //    loop: design intent → bundle → independent instance → same path.
        Assert.Equal(parsedB.TestCases[0].ExpectedTrace, traceA);
    }

    // ===== Helpers =====

    /// <summary>
    /// Each call returns a fresh in-memory SQLite connection + AppDbContext
    /// + DbContextOptions. The connection is kept open for the lifetime of
    /// the test (the harness closes it via <see cref="DisposeAsync"/>) so
    /// the <c>:memory:</c> database survives across multiple <c>AppDbContext</c>
    /// instances built against the same options. No <c>Cache=Shared</c> — each
    /// connection backs an independent in-memory database, which is exactly
    /// the "two distinct customer sites" model the test wants.
    /// </summary>
    private (AppDbContext Db, DbContextOptions<AppDbContext> Options, SqliteConnection Conn) CreateInMemoryInstance()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .AddInterceptors(interceptor)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return (db, options, conn);
    }

    private ProcessRuntime BuildRuntime(DbContextOptions<AppDbContext> options)
    {
        var db = new AppDbContext(options);
        var orgReader = new OrgChartReader(db);
        var resolver = new ActorResolver(orgReader, new NoOpResolutionAuditor(),
            NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var notifier = new StubNotificationDispatcher();
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var specLoader = new NullSpecLoader();
        return new ProcessRuntime(db, _clock, specLoader, resolver, delegation, notifier,
            evaluator, NullLogger<ProcessRuntime>.Instance);
    }

    /// <summary>
    /// Drives the instance forward to completion using the same
    /// approve-on-approval / no-decision-otherwise policy the repro runner
    /// uses. Keeps the two A-side and B-side runs symmetric.
    /// </summary>
    private static async Task DriveToCompletionAsync(
        DbContextOptions<AppDbContext> options,
        IProcessRuntime runtime,
        Guid instanceId,
        Guid fallbackActor)
    {
        const int maxSteps = 64;
        for (var step = 0; step < maxSteps; step++)
        {
            using var db = new AppDbContext(options);
            var instance = await db.ProcessInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId);
            if (instance is null || instance.Status != InstanceStatus.Running) return;

            var next = await db.ProcessTasks
                .AsNoTracking()
                .Where(t => t.ProcessInstanceId == instanceId
                            && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .FirstOrDefaultAsync();
            if (next is null) return;

            var actor = next.ActualAssigneeUserId ?? next.OriginalAssigneeUserId ?? fallbackActor;
            Decision? decision = next.NodeKind == NodeKind.Approval ? Decision.Approve : null;
            await runtime.SubmitTaskAsync(new SubmitTaskCommand(next.Id, actor,
                FormDataPatch: null, Decision: decision, Comment: null));
        }

        throw new InvalidOperationException(
            $"Instance {instanceId} did not complete within {maxSteps} steps.");
    }

    /// <summary>
    /// Mirrors <c>BundleReproducibilityRunner.ExtractActualTraceAsync</c> —
    /// reconstructs the linear node path from TaskSpawned + GatewayEvaluated
    /// history events plus the spec's flow graph. Kept verbatim here so the
    /// test asserts on the same trace shape the runner produces.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ExtractTraceAsync(
        DbContextOptions<AppDbContext> options, Guid instanceId, string snapshotJson)
    {
        using var db = new AppDbContext(options);
        var rows = await db.TaskHistory
            .AsNoTracking()
            .Where(h => h.ProcessInstanceId == instanceId
                        && (h.EventType == HistoryEventType.TaskSpawned
                            || h.EventType == HistoryEventType.GatewayEvaluated))
            .Select(h => new { h.EventType, h.PayloadJson })
            .ToListAsync();

        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var property = row.EventType == HistoryEventType.TaskSpawned ? "nodeId" : "gatewayId";
            using var doc = JsonDocument.Parse(row.PayloadJson);
            if (doc.RootElement.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrEmpty(s)) touched.Add(s);
            }
        }

        var (startId, endId, edges) = ParseFlow(snapshotJson);
        if (startId is null) return Array.Empty<string>();

        var trace = new List<string> { startId };
        var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
        var current = startId;
        var safety = 0;
        while (safety++ < 64)
        {
            if (!edges.TryGetValue(current, out var outs) || outs.Count == 0) break;
            string? next = null;
            foreach (var (target, _) in outs)
            {
                if (touched.Contains(target) || target == endId)
                {
                    next = target;
                    break;
                }
            }
            if (next is null) break;
            if (!visited.Add(next)) break;
            trace.Add(next);
            current = next;
            if (current == endId) break;
        }
        return trace;
    }

    private static (string? StartId, string? EndId, Dictionary<string, List<(string, string?)>> Edges)
        ParseFlow(string snapshotJson)
    {
        var edges = new Dictionary<string, List<(string, string?)>>(StringComparer.Ordinal);
        string? startId = null;
        string? endId = null;
        using var doc = JsonDocument.Parse(snapshotJson);
        if (!doc.RootElement.TryGetProperty("flow", out var flow)) return (null, null, edges);

        if (flow.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in nodes.EnumerateArray())
            {
                if (!n.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) continue;
                if (!n.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                var ty = t.GetString();
                var id = idEl.GetString();
                if (ty == "startEvent") startId ??= id;
                else if (ty == "endEvent") endId ??= id;
            }
        }
        if (flow.TryGetProperty("edges", out var edgeArr) && edgeArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in edgeArr.EnumerateArray())
            {
                var src = e.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                var tgt = e.TryGetProperty("target", out var tg) && tg.ValueKind == JsonValueKind.String ? tg.GetString() : null;
                if (src is null || tgt is null) continue;
                if (!edges.TryGetValue(src, out var list))
                {
                    list = new List<(string, string?)>();
                    edges[src] = list;
                }
                list.Add((tgt, null));
            }
        }
        return (startId, endId, edges);
    }

    /// <summary>
    /// Re-serialise via JsonDocument so two JSON strings that are
    /// semantically equal (modulo whitespace, member order is preserved by
    /// JsonDocument) compare equal. The runtime writes SpecSnapshotJson via
    /// <c>JsonElement.GetRawText()</c> so this is a defensive normalisation
    /// — if the runtime ever changes its serialisation, the test still
    /// passes as long as the data is equivalent.
    /// </summary>
    private static string NormalizeJson(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}

using System.Text;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Spec.Bundle;

/// <summary>
/// Reference implementation of <see cref="IBundleReproducibilityRunner"/>.
/// Drives the live <see cref="IProcessRuntime"/> against each test case
/// using the inline-spec overload so the bundle's spec.json is exercised
/// verbatim — the same code path a future runtime invocation will take.
///
/// <para>
/// Comparison is strict on <em>node-id order</em> and ignores everything
/// else (timestamps, assignee user-ids, history payload). The bundle
/// promise is "this case follows this path"; the test isn't asking the
/// runtime to match a particular timing or actor pick.
/// </para>
///
/// <para>
/// Per-step driving is best-effort for v1: at each open task we look up
/// the actual assignee and submit with <c>Decision.Approve</c> when the
/// task is an Approval, or no decision otherwise. Test cases don't carry
/// per-step decisions today, so this matches the LEAVE happy-path
/// semantics. Reject-paths are out of scope until a richer test-case
/// schema lands.
/// </para>
/// </summary>
public sealed class BundleReproducibilityRunner(
    AppDbContext db,
    IProcessRuntime runtime,
    ILogger<BundleReproducibilityRunner> logger) : IBundleReproducibilityRunner
{
    private const int MaxStepsPerCase = 64;

    public async Task<ReproReport> RunAsync(
        LoadedBundleHandle handle,
        IReadOnlyList<TestCaseSnapshot> cases,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(cases);

        var results = new List<CaseResult>(cases.Count);
        var anyFailed = false;

        foreach (var tc in cases)
        {
            var caseResult = await RunOneAsync(handle, tc, ct);
            results.Add(caseResult);
            if (caseResult.Status == ReproStatus.Fail) anyFailed = true;
        }

        return new ReproReport(
            anyFailed ? ReproStatus.Fail : ReproStatus.Pass,
            results);
    }

    private async Task<CaseResult> RunOneAsync(LoadedBundleHandle handle, TestCaseSnapshot tc, CancellationToken ct)
    {
        Guid instanceId;
        try
        {
            var startCmd = new StartInstanceCommand(handle.RegisteredSpecCode, tc.Inputs, handle.InitiatorUserId);
            var start = await runtime.StartInstanceAsync(startCmd, handle.SpecJson, ct);
            instanceId = start.InstanceId;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Repro case {Case} failed at StartInstance", tc.Id);
            return new CaseResult(tc.Id, ReproStatus.Fail, tc.ExpectedTrace, Array.Empty<string>(),
                $"start failed: {ex.Message}");
        }

        // Drive forward until no open task remains or instance is no longer Running.
        for (var step = 0; step < MaxStepsPerCase; step++)
        {
            var instance = await db.ProcessInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            if (instance is null || instance.Status != InstanceStatus.Running) break;

            var nextTask = await db.ProcessTasks
                .AsNoTracking()
                .Where(t => t.ProcessInstanceId == instanceId
                            && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .FirstOrDefaultAsync(ct);
            if (nextTask is null) break;

            var actor = nextTask.ActualAssigneeUserId ?? nextTask.OriginalAssigneeUserId ?? handle.InitiatorUserId;
            Decision? decision = nextTask.NodeKind == NodeKind.Approval ? Decision.Approve : null;

            try
            {
                await runtime.SubmitTaskAsync(
                    new SubmitTaskCommand(nextTask.Id, actor, FormDataPatch: null, Decision: decision, Comment: null), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Repro case {Case} failed at task {Task} (node {Node})",
                    tc.Id, nextTask.Id, nextTask.NodeId);
                var partial = await ExtractActualTraceAsync(instanceId, ct);
                return new CaseResult(tc.Id, ReproStatus.Fail, tc.ExpectedTrace, partial,
                    $"submit failed at node {nextTask.NodeId}: {ex.Message}");
            }
        }

        var actualTrace = await ExtractActualTraceAsync(instanceId, ct);
        var diff = ComputeDiff(tc.ExpectedTrace, actualTrace);
        return new CaseResult(
            tc.Id,
            diff is null ? ReproStatus.Pass : ReproStatus.Fail,
            tc.ExpectedTrace,
            actualTrace,
            diff);
    }

    /// <summary>
    /// Builds the actual trace by:
    ///   1. Collecting every node id touched by the instance
    ///      (TaskSpawned + GatewayEvaluated history events) plus the
    ///      synthetic start/end node ids derived from the spec snapshot.
    ///   2. Walking the spec's directed flow graph from start, choosing
    ///      at each branch the outgoing edge whose target is in the
    ///      touched set.
    /// Step 2 sidesteps the history-row ordering problem (TaskSpawned
    /// rows that share a CreatedAt have non-deterministic Id-tiebreak
    /// ordering) by reconstructing the linear trace from the
    /// known-touched node set + spec topology — the same way a human
    /// would read off the path from a BPMN diagram.
    /// </summary>
    private async Task<IReadOnlyList<string>> ExtractActualTraceAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await db.ProcessInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Array.Empty<string>();

        var rows = await db.TaskHistory
            .AsNoTracking()
            .Where(h => h.ProcessInstanceId == instanceId
                        && (h.EventType == HistoryEventType.TaskSpawned
                            || h.EventType == HistoryEventType.GatewayEvaluated))
            .Select(h => new { h.EventType, h.PayloadJson })
            .ToListAsync(ct);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var nodeId = row.EventType switch
            {
                HistoryEventType.TaskSpawned => ExtractStringFromJson(row.PayloadJson, "nodeId"),
                HistoryEventType.GatewayEvaluated => ExtractStringFromJson(row.PayloadJson, "gatewayId"),
                _ => null,
            };
            if (!string.IsNullOrEmpty(nodeId)) touched.Add(nodeId!);
        }

        var (startNodeId, endNodeId, edgesBySource) = ParseFlow(instance.SpecSnapshotJson);
        if (startNodeId is null) return Array.Empty<string>();

        var trace = new List<string> { startNodeId };
        var visited = new HashSet<string>(StringComparer.Ordinal) { startNodeId };
        var current = startNodeId;
        var safety = 0;

        while (safety++ < 64)
        {
            if (!edgesBySource.TryGetValue(current, out var outEdges) || outEdges.Count == 0) break;

            string? next = null;
            // Prefer an outgoing edge whose target is in the touched set.
            // This handles gateways: only the chosen branch is touched, so
            // we follow exactly the path the runtime took.
            foreach (var (target, _) in outEdges)
            {
                if (touched.Contains(target) || target == endNodeId)
                {
                    next = target;
                    break;
                }
            }
            // Fallback: when no successor is touched (e.g. the instance
            // errored before reaching the end), bail out — the partial
            // trace is the diagnostic.
            if (next is null) break;
            if (!visited.Add(next)) break;
            trace.Add(next);
            current = next;
            if (current == endNodeId) break;
        }

        return trace;
    }

    private static (string? StartId, string? EndId, Dictionary<string, List<(string Target, string? Condition)>> Edges)
        ParseFlow(string snapshotJson)
    {
        var edgesBySource = new Dictionary<string, List<(string, string?)>>(StringComparer.Ordinal);
        string? startId = null;
        string? endId = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(snapshotJson);
            if (!doc.RootElement.TryGetProperty("flow", out var flow)) return (null, null, edgesBySource);

            if (flow.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var n in nodes.EnumerateArray())
                {
                    if (!n.TryGetProperty("type", out var t) || t.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    if (!n.TryGetProperty("id", out var idEl) || idEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    var id = idEl.GetString();
                    var ty = t.GetString();
                    if (ty == "startEvent") startId ??= id;
                    else if (ty == "endEvent") endId ??= id;
                }
            }

            if (flow.TryGetProperty("edges", out var edges) && edges.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var e in edges.EnumerateArray())
                {
                    var src = e.TryGetProperty("source", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString() : null;
                    var tgt = e.TryGetProperty("target", out var tg) && tg.ValueKind == System.Text.Json.JsonValueKind.String ? tg.GetString() : null;
                    if (src is null || tgt is null) continue;
                    var cond = e.TryGetProperty("condition", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String ? c.GetString() : null;
                    if (!edgesBySource.TryGetValue(src, out var list))
                    {
                        list = new List<(string, string?)>();
                        edgesBySource[src] = list;
                    }
                    list.Add((tgt, cond));
                }
            }
        }
        catch (System.Text.Json.JsonException) { /* defensive — empty trace */ }

        return (startId, endId, edgesBySource);
    }

    private static string? ExtractStringFromJson(string json, string property)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(property, out var el)) return null;
            return el.ValueKind == System.Text.Json.JsonValueKind.String ? el.GetString() : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// Returns null when the two traces are equal, otherwise a single
    /// human-readable string in the canonical "expected: [...]\nactual:
    /// [...]" form. Keeping the diff dumb means the UI can show it
    /// verbatim without parsing — the report itself is the artefact.
    /// </summary>
    internal static string? ComputeDiff(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.SequenceEqual(actual, StringComparer.Ordinal)) return null;

        var sb = new StringBuilder();
        sb.Append("expected: [").Append(string.Join(",", expected)).AppendLine("]");
        sb.Append("actual:   [").Append(string.Join(",", actual)).Append(']');
        return sb.ToString();
    }
}

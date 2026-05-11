using System.Text;
using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Emits a step-by-step happy-path narration for the bundled flow. Two
/// modes:
///
/// <list type="bullet">
///   <item><description>If a test case is supplied, walk its <c>ExpectedTrace</c>
///   against the spec to enrich each node with its label + permissions hints.</description></item>
///   <item><description>If no test case is supplied, walk the spec's first
///   connected path from the (single) <c>startEvent</c> to the first <c>endEvent</c>,
///   following the <c>isDefault</c> branch at any gateway.</description></item>
///  </list>
///
/// Used during sales demos so the audience sees a narrative version of
/// the flow alongside the BPMN diagram.
/// </summary>
public sealed class WalkthroughRenderer
{
    public string Render(JsonElement spec, TestCaseSnapshot? testCase)
    {
        var sb = new StringBuilder();
        var (nodeIndex, edgeAdjacency, defaultBranches) = BuildIndexes(spec);
        var userTaskById = BuildUserTaskIndex(spec);
        var approvalById = BuildApprovalIndex(spec);

        IReadOnlyList<string> trace;
        string title;
        if (testCase is not null && testCase.ExpectedTrace.Count > 0)
        {
            trace = testCase.ExpectedTrace;
            title = $"Happy path: {testCase.Name}";
        }
        else
        {
            trace = WalkDefaultPath(nodeIndex, edgeAdjacency, defaultBranches);
            title = "Happy path: default route";
        }

        sb.Append("# ").AppendLine(title);
        sb.AppendLine();

        var step = 1;
        foreach (var nodeId in trace)
        {
            var (label, type) = nodeIndex.TryGetValue(nodeId, out var info) ? info : (nodeId, "?");
            var who = DescribeWho(nodeId, type, userTaskById, approvalById);
            var what = DescribeWhat(type);
            sb.Append(step).Append(". **").Append(label).Append("** (`").Append(nodeId).Append("`) - ").Append(who).Append(' ').AppendLine(what);
            step++;
        }

        return sb.ToString();
    }

    private static (Dictionary<string, (string Label, string Type)> Nodes,
                    Dictionary<string, List<(string Target, string? Condition, bool IsDefault)>> Edges,
                    Dictionary<string, string> DefaultEdgeTarget) BuildIndexes(JsonElement spec)
    {
        var nodes = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<(string, string?, bool)>>(StringComparer.Ordinal);
        var defaultTarget = new Dictionary<string, string>(StringComparer.Ordinal);

        if (spec.TryGetProperty("flow", out var flow) && flow.ValueKind == JsonValueKind.Object)
        {
            if (flow.TryGetProperty("nodes", out var nArr) && nArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var n in nArr.EnumerateArray())
                {
                    var id = GetString(n, "id"); if (id is null) continue;
                    var label = GetString(n, "label") ?? id;
                    var type = GetString(n, "type") ?? "?";
                    nodes[id] = (label, type);
                }
            }
            if (flow.TryGetProperty("edges", out var eArr) && eArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in eArr.EnumerateArray())
                {
                    var src = GetString(e, "source"); var tgt = GetString(e, "target");
                    if (src is null || tgt is null) continue;
                    var cond = GetString(e, "condition");
                    var isDefault = e.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True;
                    if (!edges.TryGetValue(src, out var list)) edges[src] = list = new();
                    list.Add((tgt, cond, isDefault));
                    if (isDefault) defaultTarget[src] = tgt;
                }
            }
        }
        return (nodes, edges, defaultTarget);
    }

    private static Dictionary<string, JsonElement> BuildUserTaskIndex(JsonElement spec)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (spec.TryGetProperty("userTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tasks.EnumerateArray())
            {
                var id = GetString(t, "id");
                if (id is not null) map[id] = t;
            }
        }
        return map;
    }

    private static Dictionary<string, JsonElement> BuildApprovalIndex(JsonElement spec)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (spec.TryGetProperty("approvals", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                var id = GetString(a, "id");
                if (id is not null) map[id] = a;
            }
        }
        return map;
    }

    private static List<string> WalkDefaultPath(
        Dictionary<string, (string Label, string Type)> nodes,
        Dictionary<string, List<(string Target, string? Condition, bool IsDefault)>> edges,
        Dictionary<string, string> defaultTarget)
    {
        var trace = new List<string>();
        var startId = nodes.FirstOrDefault(kv => kv.Value.Type == "startEvent").Key;
        if (startId is null) return trace;

        var current = startId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current is not null && seen.Add(current))
        {
            trace.Add(current);
            var (_, type) = nodes[current];
            if (type == "endEvent") break;
            if (!edges.TryGetValue(current, out var outs) || outs.Count == 0) break;
            var next = defaultTarget.TryGetValue(current, out var dt) ? dt : outs[0].Target;
            current = next;
        }
        return trace;
    }

    private static string DescribeWho(
        string nodeId,
        string type,
        Dictionary<string, JsonElement> userTasks,
        Dictionary<string, JsonElement> approvals)
    {
        switch (type)
        {
            case "userTask":
                if (userTasks.TryGetValue(nodeId, out var ut)
                    && ut.TryGetProperty("permissions", out var perms)
                    && perms.TryGetProperty("submitter", out var sub)
                    && sub.ValueKind == JsonValueKind.String)
                {
                    return $"submitter `{sub.GetString()}`";
                }
                return "submitter";
            case "approval":
                if (approvals.TryGetValue(nodeId, out var a)
                    && a.TryGetProperty("approver", out var ap))
                {
                    return $"approver `{SummarizeActor(ap)}`";
                }
                return "approver";
            case "startEvent": return "system";
            case "endEvent": return "system";
            case "gateway": return "gateway";
            default: return type;
        }
    }

    private static string DescribeWhat(string type) => type switch
    {
        "startEvent" => "starts the case.",
        "userTask" => "fills out the form and submits.",
        "approval" => "reviews and approves.",
        "gateway" => "routes the case based on form data.",
        "endEvent" => "completes the case.",
        _ => "performs the step.",
    };

    private static string SummarizeActor(JsonElement actor)
    {
        if (actor.ValueKind != JsonValueKind.Object) return "?";
        var type = GetString(actor, "type") ?? "?";
        return type switch
        {
            "expr" => $"expr:{GetString(actor, "path")}",
            "role" => $"role:{GetString(actor, "code")}",
            "group" => $"group:{GetString(actor, "code")}",
            "user" => $"user:{GetString(actor, "id")}",
            _ => type,
        };
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}

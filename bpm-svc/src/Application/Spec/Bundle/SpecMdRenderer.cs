using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Turns a draft <c>spec.json</c> into a human-readable markdown overview
/// (<c>spec.md</c>) for the View tab in Flow Library and for sales demos
/// where opening raw JSON would lose the audience. Pure / stateless.
/// </summary>
public sealed class SpecMdRenderer
{
    public string Render(JsonElement spec)
    {
        var sb = new StringBuilder();

        var meta = TryGet(spec, "meta");
        var flowName = meta is { } m && m.TryGetProperty("flowName", out var fn) ? fn.GetString() ?? "(unnamed flow)" : "(unnamed flow)";
        var flowCode = meta is { } m2 && m2.TryGetProperty("flowCode", out var fc) ? fc.GetString() ?? "?" : "?";
        var flowVersion = meta is { } m3 && m3.TryGetProperty("flowVersion", out var fv) && fv.ValueKind == JsonValueKind.Number ? fv.GetInt32().ToString(CultureInfo.InvariantCulture) : "?";

        sb.Append("# ").Append(flowName).Append(" (").Append(flowCode).Append(" v").Append(flowVersion).AppendLine(")");
        sb.AppendLine();

        if (meta is { } m4 && m4.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
        {
            sb.AppendLine(desc.GetString());
            sb.AppendLine();
        }

        RenderFlowNodes(spec, sb);
        RenderUserTasks(spec, sb);
        RenderDecisions(spec, sb);
        RenderApprovers(spec, sb);
        RenderNotifications(spec, sb);
        RenderSla(spec, sb);
        RenderActors(spec, sb);

        return sb.ToString();
    }

    private static void RenderFlowNodes(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## Flow nodes");
        var nodes = TryGet(spec, "flow", "nodes");
        if (nodes is null || nodes.Value.ValueKind != JsonValueKind.Array)
        {
            sb.AppendLine("- (none)");
            sb.AppendLine();
            return;
        }
        foreach (var n in nodes.Value.EnumerateArray())
        {
            var id = GetString(n, "id") ?? "?";
            var type = GetString(n, "type") ?? "?";
            var label = GetString(n, "label") ?? "";
            sb.Append("- ").Append(id).Append(" [").Append(type).Append("]: ").AppendLine(label);
        }
        sb.AppendLine();
    }

    private static void RenderUserTasks(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## User tasks");
        if (!spec.TryGetProperty("userTasks", out var tasks) || tasks.ValueKind != JsonValueKind.Array)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        foreach (var t in tasks.EnumerateArray())
        {
            var id = GetString(t, "id") ?? "?";
            sb.Append("### ").AppendLine(id);
            if (t.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Object)
            {
                if (perms.TryGetProperty("submitter", out var sub) && sub.ValueKind == JsonValueKind.String)
                {
                    sb.Append("- submitter: ").AppendLine(sub.GetString());
                }
            }
            if (t.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                var ids = fields.EnumerateArray()
                    .Select(f => GetString(f, "id"))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                sb.Append("- fields: ").AppendLine(string.Join(", ", ids));
            }
            sb.AppendLine();
        }
    }

    private static void RenderDecisions(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## Decisions / Gateways");
        if (!spec.TryGetProperty("decisions", out var decisions) || decisions.ValueKind != JsonValueKind.Array || decisions.GetArrayLength() == 0)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        foreach (var d in decisions.EnumerateArray())
        {
            var id = GetString(d, "id") ?? "?";
            var type = GetString(d, "type") ?? "?";
            sb.Append("- ").Append(id).Append(" (").Append(type).AppendLine(")");
            if (d.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in branches.EnumerateArray())
                {
                    var edge = GetString(b, "edgeId") ?? "?";
                    var cond = GetString(b, "condition") ?? "";
                    sb.Append("  - edge ").Append(edge).Append(": `").Append(cond).AppendLine("`");
                }
            }
        }
        sb.AppendLine();
    }

    private static void RenderApprovers(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## Approvers");
        if (!spec.TryGetProperty("approvals", out var approvals) || approvals.ValueKind != JsonValueKind.Array || approvals.GetArrayLength() == 0)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        foreach (var a in approvals.EnumerateArray())
        {
            var id = GetString(a, "id") ?? "?";
            sb.Append("- ").Append(id).Append(": ").AppendLine(SummarizeActor(a.TryGetProperty("approver", out var ap) ? ap : default));
        }
        sb.AppendLine();
    }

    private static void RenderNotifications(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## Notifications");
        if (!spec.TryGetProperty("notifications", out var notifs) || notifs.ValueKind != JsonValueKind.Array || notifs.GetArrayLength() == 0)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        foreach (var n in notifs.EnumerateArray())
        {
            var id = GetString(n, "id") ?? "?";
            var trigger = GetString(n, "trigger") ?? "?";
            sb.Append("- ").Append(id).Append(" (").Append(trigger).AppendLine(")");
        }
        sb.AppendLine();
    }

    private static void RenderSla(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## SLA");
        if (!spec.TryGetProperty("sla", out var sla) || sla.ValueKind != JsonValueKind.Object)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        if (sla.TryGetProperty("perNode", out var perNode) && perNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in perNode.EnumerateObject())
            {
                var dur = prop.Value.TryGetProperty("duration", out var du) ? du.GetString() : "";
                sb.Append("- ").Append(prop.Name).Append(": ").AppendLine(dur);
            }
        }
        sb.AppendLine();
    }

    private static void RenderActors(JsonElement spec, StringBuilder sb)
    {
        sb.AppendLine("## Actors used");
        var refs = ActorRefCollector.Collect(spec);
        if (refs.Count == 0)
        {
            sb.AppendLine("(none)");
            sb.AppendLine();
            return;
        }
        foreach (var r in refs)
        {
            sb.Append("- ").AppendLine(r);
        }
        sb.AppendLine();
    }

    private static string SummarizeActor(JsonElement actor)
    {
        if (actor.ValueKind != JsonValueKind.Object) return "(unknown)";
        var type = GetString(actor, "type") ?? "?";
        return type switch
        {
            "expr" => $"expr: {GetString(actor, "path")}",
            "role" => $"role: {GetString(actor, "code")}",
            "group" => $"group: {GetString(actor, "code")}",
            "user" => $"user: {GetString(actor, "id")}",
            _ => type,
        };
    }

    private static JsonElement? TryGet(JsonElement el, params string[] path)
    {
        var cur = el;
        foreach (var seg in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(seg, out var next))
                return null;
            cur = next;
        }
        return cur;
    }

    private static string? GetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }
}

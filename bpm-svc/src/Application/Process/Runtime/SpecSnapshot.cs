using System.Text.Json;

namespace Bpm.Application.Process.Runtime;

/// <summary>
/// Lazy view over a <c>spec.json</c> snapshot. Construction parses the JSON
/// once with <see cref="JsonDocument.Parse(string)"/>; every accessor walks
/// the document. Intended lifetime: a single command (one ProcessRuntime
/// invocation). Disposal is the caller's responsibility through
/// <see cref="IDisposable"/>.
/// </summary>
public sealed class SpecSnapshot : IDisposable
{
    private readonly JsonDocument _doc;
    public string Json { get; }
    public string FlowCode { get; }
    public string TenantCode { get; }
    public int FlowVersion { get; }

    public SpecSnapshot(string json)
    {
        Json = json;
        _doc = JsonDocument.Parse(json);

        if (_doc.RootElement.TryGetProperty("meta", out var meta))
        {
            FlowCode = meta.TryGetProperty("flowCode", out var fc) && fc.ValueKind == JsonValueKind.String
                ? fc.GetString() ?? string.Empty : string.Empty;
            TenantCode = meta.TryGetProperty("tenant", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "default" : "default";
            FlowVersion = meta.TryGetProperty("flowVersion", out var fv) && fv.ValueKind == JsonValueKind.Number
                ? fv.GetInt32() : 1;
        }
        else
        {
            FlowCode = string.Empty;
            TenantCode = "default";
            FlowVersion = 1;
        }
    }

    public JsonElement Root => _doc.RootElement;

    /// <summary>Find a node in flow.nodes by id; returns null if not present.</summary>
    public NodeDescriptor? GetNode(string nodeId)
    {
        if (!Root.TryGetProperty("flow", out var flow)) return null;
        if (!flow.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return null;

        foreach (var n in nodes.EnumerateArray())
        {
            var id = n.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (id == nodeId)
            {
                var typeStr = n.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                var label = n.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String
                    ? labelEl.GetString() : null;
                return new NodeDescriptor(id!, typeStr, label, n);
            }
        }
        return null;
    }

    public NodeDescriptor? StartNode
    {
        get
        {
            if (!Root.TryGetProperty("flow", out var flow)) return null;
            if (!flow.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return null;

            foreach (var n in nodes.EnumerateArray())
            {
                var typeStr = n.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                if (typeStr == "startEvent")
                {
                    var id = n.GetProperty("id").GetString()!;
                    var label = n.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String
                        ? labelEl.GetString() : null;
                    return new NodeDescriptor(id, typeStr, label, n);
                }
            }
            return null;
        }
    }

    public string StartNodeId => StartNode?.Id
        ?? throw new InvalidOperationException("spec has no startEvent node");

    /// <summary>All edges where source == nodeId.</summary>
    public IReadOnlyList<EdgeDescriptor> GetEdgesFrom(string nodeId)
    {
        var result = new List<EdgeDescriptor>();
        foreach (var e in Edges)
        {
            if (e.Source == nodeId) result.Add(e);
        }
        return result;
    }

    public IReadOnlyList<EdgeDescriptor> Edges
    {
        get
        {
            var list = new List<EdgeDescriptor>();
            if (!Root.TryGetProperty("flow", out var flow)) return list;
            if (!flow.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in edges.EnumerateArray())
            {
                var id = e.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var source = e.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                var target = e.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                var condition = e.TryGetProperty("condition", out var c) && c.ValueKind == JsonValueKind.String
                    ? c.GetString() : null;
                var isDefault = e.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True;
                list.Add(new EdgeDescriptor(id, source, target, condition, isDefault));
            }
            return list;
        }
    }

    public UserTaskDescriptor? GetUserTask(string id)
    {
        if (!Root.TryGetProperty("userTasks", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.GetString() == id)
            {
                string? submitter = null;
                if (item.TryGetProperty("permissions", out var perms)
                    && perms.TryGetProperty("submitter", out var sub)
                    && sub.ValueKind == JsonValueKind.String)
                {
                    submitter = sub.GetString();
                }
                return new UserTaskDescriptor(id, submitter, item);
            }
        }
        return null;
    }

    public ApprovalDescriptor? GetApproval(string id)
    {
        if (!Root.TryGetProperty("approvals", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.GetString() == id)
            {
                JsonElement? approver = null;
                if (item.TryGetProperty("approver", out var ap)) approver = ap;
                return new ApprovalDescriptor(id, approver, item);
            }
        }
        return null;
    }

    public GatewayDescriptor? GetGateway(string id)
    {
        if (!Root.TryGetProperty("decisions", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var idEl) && idEl.GetString() == id)
            {
                var branches = new List<GatewayBranch>();
                if (item.TryGetProperty("branches", out var br) && br.ValueKind == JsonValueKind.Array)
                {
                    foreach (var b in br.EnumerateArray())
                    {
                        var edgeId = b.TryGetProperty("edgeId", out var e) ? e.GetString() ?? "" : "";
                        var condition = b.TryGetProperty("condition", out var c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString() : null;
                        var isDefault = b.TryGetProperty("isDefault", out var d) && d.ValueKind == JsonValueKind.True;
                        branches.Add(new GatewayBranch(edgeId, condition, isDefault));
                    }
                }
                return new GatewayDescriptor(id, branches, item);
            }
        }
        return null;
    }

    public IReadOnlyList<NotificationDescriptor> Notifications
    {
        get
        {
            var list = new List<NotificationDescriptor>();
            if (!Root.TryGetProperty("notifications", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var n in arr.EnumerateArray())
            {
                var id = n.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var trigger = n.TryGetProperty("trigger", out var trEl) && trEl.ValueKind == JsonValueKind.String
                    ? trEl.GetString() ?? "" : "";
                list.Add(new NotificationDescriptor(id, trigger, n));
            }
            return list;
        }
    }

    public IReadOnlyList<NotificationDescriptor> GetNotifications(string trigger)
        => Notifications.Where(n => n.Trigger == trigger).ToList();

    public void Dispose() => _doc.Dispose();
}

public sealed record NodeDescriptor(string Id, string Type, string? Label, JsonElement Raw);
public sealed record EdgeDescriptor(string Id, string Source, string Target, string? Condition, bool IsDefault);
public sealed record UserTaskDescriptor(string Id, string? Submitter, JsonElement Raw);
public sealed record ApprovalDescriptor(string Id, JsonElement? Approver, JsonElement Raw);
public sealed record GatewayDescriptor(string Id, IReadOnlyList<GatewayBranch> Branches, JsonElement Raw);
public sealed record GatewayBranch(string EdgeId, string? Condition, bool IsDefault);
public sealed record NotificationDescriptor(string Id, string Trigger, JsonElement Raw);

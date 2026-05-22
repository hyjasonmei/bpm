using System.Text;
using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Diffs the new spec against the parent's spec and emits a markdown
/// changelog (Added / Removed / Changed) keyed on flow-node ids,
/// userTask form-field ids, and notification ids. Pure / stateless.
///
/// <para>
/// Takes <c>parentSpec</c> as a <see cref="JsonElement"/> directly rather
/// than a parent bundle zip — see the comment on <see cref="BundleBuildRequest.ParentSpecJson"/>
/// for why. PR-I3 + the Flow Library REST layer will compose the two.
/// </para>
/// </summary>
public sealed class ChangelogRenderer
{
    public string Render(JsonElement parentSpec, JsonElement newSpec)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Changelog vs parent bundle");
        sb.AppendLine();

        var parentNodes = NodeIds(parentSpec);
        var newNodes = NodeIds(newSpec);
        var parentTasks = UserTaskFieldMap(parentSpec);
        var newTasks = UserTaskFieldMap(newSpec);
        var parentNotifs = NotificationIds(parentSpec);
        var newNotifs = NotificationIds(newSpec);

        var addedNodes = newNodes.Except(parentNodes).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var removedNodes = parentNodes.Except(newNodes).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var addedNotifs = newNotifs.Except(parentNotifs).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var removedNotifs = parentNotifs.Except(newNotifs).OrderBy(s => s, StringComparer.Ordinal).ToList();

        var changedTasks = new List<string>();
        foreach (var (id, fields) in newTasks)
        {
            if (parentTasks.TryGetValue(id, out var oldFields) && !FieldsEqual(oldFields, fields))
                changedTasks.Add(id);
        }
        changedTasks.Sort(StringComparer.Ordinal);

        sb.AppendLine("## Added");
        if (addedNodes.Count == 0 && addedNotifs.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var n in addedNodes) sb.Append("- flow node ").AppendLine(n);
            foreach (var n in addedNotifs) sb.Append("- notification ").AppendLine(n);
        }
        sb.AppendLine();

        sb.AppendLine("## Removed");
        if (removedNodes.Count == 0 && removedNotifs.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var n in removedNodes) sb.Append("- flow node ").AppendLine(n);
            foreach (var n in removedNotifs) sb.Append("- notification ").AppendLine(n);
        }
        sb.AppendLine();

        sb.AppendLine("## Changed");
        if (changedTasks.Count == 0)
        {
            sb.AppendLine("- (none)");
        }
        else
        {
            foreach (var n in changedTasks) sb.Append("- userTask ").Append(n).AppendLine(" form fields");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static HashSet<string> NodeIds(JsonElement spec)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (spec.TryGetProperty("flow", out var flow)
            && flow.TryGetProperty("nodes", out var nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in nodes.EnumerateArray())
            {
                if (n.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var s = id.GetString();
                    if (!string.IsNullOrEmpty(s)) set.Add(s!);
                }
            }
        }
        return set;
    }

    private static HashSet<string> NotificationIds(JsonElement spec)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (spec.TryGetProperty("notifications", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in arr.EnumerateArray())
            {
                if (n.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    var s = id.GetString();
                    if (!string.IsNullOrEmpty(s)) set.Add(s!);
                }
            }
        }
        return set;
    }

    private static Dictionary<string, string> UserTaskFieldMap(JsonElement spec)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (spec.TryGetProperty("userTasks", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                if (!t.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                var key = id.GetString() ?? "";
                if (string.IsNullOrEmpty(key)) continue;
                map[key] = t.TryGetProperty("fields", out var fields) ? fields.GetRawText() : "[]";
            }
        }
        return map;
    }

    private static bool FieldsEqual(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}

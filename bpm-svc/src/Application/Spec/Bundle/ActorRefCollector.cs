using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Walks a draft spec and pulls out every distinct actor reference into a
/// stable, sorted list. Drives both the <c>## Actors used</c> section in
/// <c>spec.md</c> and the <c>actors.json</c> bundle file. Centralizing
/// here keeps the two outputs in lockstep — the parser-side validator
/// (PR-I3) compares against the same set.
/// </summary>
internal static class ActorRefCollector
{
    public static IReadOnlyList<string> Collect(JsonElement spec)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);

        // approvals[].approver
        if (spec.TryGetProperty("approvals", out var approvals) && approvals.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in approvals.EnumerateArray())
            {
                if (a.TryGetProperty("approver", out var approver))
                    WalkActor(approver, set);
            }
        }

        // notifications[].recipients[]
        if (spec.TryGetProperty("notifications", out var notifs) && notifs.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in notifs.EnumerateArray())
            {
                if (n.TryGetProperty("recipients", out var recipients) && recipients.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in recipients.EnumerateArray())
                        WalkActor(r, set);
                }
            }
        }

        // userTasks[].permissions.{submitter,viewers,editors}
        if (spec.TryGetProperty("userTasks", out var userTasks) && userTasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var ut in userTasks.EnumerateArray())
            {
                if (!ut.TryGetProperty("permissions", out var perms) || perms.ValueKind != JsonValueKind.Object)
                    continue;
                AddStringList(perms, "submitter", set);
                AddStringList(perms, "viewers", set);
                AddStringList(perms, "editors", set);
            }
        }

        return set.ToList();
    }

    private static void AddStringList(JsonElement parent, string prop, SortedSet<string> set)
    {
        if (!parent.TryGetProperty(prop, out var v)) return;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s)) set.Add(s!);
        }
        else if (v.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in v.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) set.Add(s!);
                }
            }
        }
    }

    private static void WalkActor(JsonElement actor, SortedSet<string> set)
    {
        if (actor.ValueKind != JsonValueKind.Object) return;
        if (!actor.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return;
        var type = typeEl.GetString();

        switch (type)
        {
            case "expr":
                if (actor.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
                    set.Add($"expr:{path.GetString()}");
                if (actor.TryGetProperty("fallback", out var fb))
                    WalkActor(fb, set);
                break;
            case "role":
                if (actor.TryGetProperty("code", out var rc) && rc.ValueKind == JsonValueKind.String)
                    set.Add($"role:{rc.GetString()}");
                break;
            case "group":
                if (actor.TryGetProperty("code", out var gc) && gc.ValueKind == JsonValueKind.String)
                    set.Add($"group:{gc.GetString()}");
                break;
            case "user":
                if (actor.TryGetProperty("id", out var uid) && uid.ValueKind == JsonValueKind.String)
                    set.Add($"user:{uid.GetString()}");
                break;
            case "current_approver":
            case "submitter":
                set.Add(type);
                break;
            case "conditional":
                if (actor.TryGetProperty("then", out var th)) WalkActor(th, set);
                if (actor.TryGetProperty("else", out var el)) WalkActor(el, set);
                break;
            case "collection":
                if (actor.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    foreach (var i in items.EnumerateArray()) WalkActor(i, set);
                break;
        }
    }
}

namespace Bpm.ChefAgent;

/// <summary>
/// Pure registry consistency checks (unit-tested, no I/O):
///
/// 1. <see cref="VisibilityIssue"/> — after a deploy is marked Published,
///    assert the flow actually WINS launcher resolution. Mirrors bpm-ui's
///    per-code rule (highest version; same version → Published, then newest
///    UpdatedAt) so a stale/shadow row is caught by the pipeline instead of
///    being discovered by an employee staring at an empty launcher.
///
/// 2. <see cref="MissingRegistrations"/> — flows whose runtime code is
///    deployed on bpm-svc but that the registry doesn't cover at (or above)
///    the deployed version. These entered the stack outside the AI Kitchen
///    pipeline (hand-merged code); the agent notifies so a human decides
///    whether to register-shipped or ignore. Detection only — no auto-write,
///    so a test/carrier flow can never auto-appear in the launcher.
/// </summary>
public static class RegistryReconciler
{
    /// <summary>Same tie-break as bpm-ui's useFlowRegistry: higher version wins;
    /// equal version → Published wins; still tied → newer UpdatedAt.</summary>
    public static RegistryCodeRow Prefer(RegistryCodeRow a, RegistryCodeRow b)
    {
        if (a.Version != b.Version) return b.Version > a.Version ? b : a;
        var aPub = IsPublished(a);
        var bPub = IsPublished(b);
        if (aPub != bPub) return bPub ? b : a;
        return b.UpdatedAt >= a.UpdatedAt ? b : a;
    }

    /// <summary>Null when (flowCode, version) wins launcher resolution as
    /// Published; otherwise a human-readable reason for MarkPublishFailed.</summary>
    public static string? VisibilityIssue(IReadOnlyList<RegistryCodeRow> rows, string flowCode, int version)
    {
        RegistryCodeRow? winner = null;
        foreach (var r in rows)
        {
            if (!string.Equals(r.FlowCode, flowCode, StringComparison.OrdinalIgnoreCase)) continue;
            winner = winner is null ? r : Prefer(winner, r);
        }
        if (winner is null)
            return $"launcher visibility check: no registry row exists for {flowCode}";
        if (!IsPublished(winner))
            return $"launcher visibility check: {flowCode} resolves to v{winner.Version} in state {winner.State} — the flow is not visible to employees";
        if (winner.Version != version)
            return $"launcher visibility check: {flowCode} resolves to v{winner.Version}, not the just-published v{version}";
        return null;
    }

    /// <summary>Deployed flows the registry doesn't cover at (or above) the
    /// deployed version — candidates for register-shipped. Sorted for a stable
    /// notification message.</summary>
    public static List<string> MissingRegistrations(
        IReadOnlyList<DeployedFlowCode> deployed,
        IReadOnlyList<RegistryCodeRow> registry)
    {
        var maxRegistered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in registry)
        {
            if (!maxRegistered.TryGetValue(r.FlowCode, out var v) || r.Version > v)
                maxRegistered[r.FlowCode] = r.Version;
        }

        var missing = new List<string>();
        foreach (var d in deployed)
        {
            if (string.IsNullOrWhiteSpace(d.FlowCode)) continue;
            if (!maxRegistered.TryGetValue(d.FlowCode, out var v) || v < d.Version)
                missing.Add($"{d.FlowCode.ToUpperInvariant()} v{d.Version}");
        }
        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    private static bool IsPublished(RegistryCodeRow r)
        => string.Equals(r.State, "Published", StringComparison.OrdinalIgnoreCase);
}

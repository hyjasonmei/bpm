using System.Text.Json;

namespace Bpm.ChefAgent;

/// <summary>
/// Persisted across one-shot runs (the agent exits after each poll, so this
/// JSON file is its only memory): stalled-cook retry counts, per-env
/// consecutive failure counts, and last-alert timestamps for cooldowns.
/// </summary>
public sealed class AgentState
{
    public Dictionary<string, int> EnvFailures { get; set; } = new();     // env name → consecutive unreachable count
    public Dictionary<string, DateTime> LastEnvAlertAt { get; set; } = new();
    public Dictionary<string, DateTime> LastRemindedAt { get; set; } = new(); // flowId → last "branch ready, merge manually" ping

    // ── Deploy worker (PublishManager) ──────────────────────────────────────
    public string? DeployInFlightFlowId { get; set; }                          // single-flight: a deploy is mid-run (set→deploy→clear)
    public Dictionary<string, DateTime> LastDeployAttemptAt { get; set; } = new(); // flowId → last deploy attempt (RetryCooldown gate)

    // ── Registry reconciliation ──────────────────────────────────────────────
    public Dictionary<string, DateTime> LastUnregisteredAlertAt { get; set; } = new(); // env name → last "deployed but unregistered" ping
    public Dictionary<string, string> LastUnregisteredSet { get; set; } = new();       // env name → set last alerted on (change → re-alert immediately)

    public static AgentState Load(string path)
    {
        if (!File.Exists(path)) return new AgentState();
        try
        {
            return JsonSerializer.Deserialize<AgentState>(File.ReadAllText(path)) ?? new AgentState();
        }
        catch
        {
            return new AgentState();   // corrupt state file → start fresh, never crash the poll
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

using System.Text.Json;

namespace Bpm.ChefAgent;

/// <summary>One admin-svc environment the agent polls.</summary>
public sealed record EnvTarget(string Name, string BaseUrl, string ChefToken, bool Enabled);

public sealed record TelegramConfig(string BotToken, string ChatId);

/// <summary>
/// Agent configuration, loaded from a JSON file path passed as argv[0].
/// Secrets (chef tokens, TG bot token) live here; the file is gitignored.
/// </summary>
public sealed record AgentConfig(
    List<EnvTarget> Environments,
    TelegramConfig? Telegram,
    string RepoPath,            // local bpm repo root (git worktree source)
    string WorktreeRoot,        // where per-cook worktrees + logs + state live
    string ClaudeBin,           // "claude"
    int MaxTurns,               // claude -p --max-turns
    int MaxSessionMinutes,      // wall-clock kill switch per cook
    int MaxAutoRetries,         // stalled-cook auto-retry ceiling (1)
    string LockFilePath,        // global single-instance lock
    string StateFilePath)       // persisted retry / failure counters
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static AgentConfig Load(string path) =>
        JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Opts)
        ?? throw new InvalidOperationException($"empty or invalid config: {path}");

    public IEnumerable<EnvTarget> EnabledEnvironments => Environments.Where(e => e.Enabled);
}

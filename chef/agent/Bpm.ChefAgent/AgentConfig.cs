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
    int MaxTurns,               // claude -p --max-turns PER SESSION (chunk size, not a hard finish limit)
    int MaxSessionMinutes,      // wall-clock kill switch per session
    int MaxCookSessions,        // absolute cap on resume sessions for one flow (runaway guard)
    int MaxNoProgressSessions,  // consecutive no-progress sessions before giving up (→ OnHold)
    string LockFilePath,        // global single-instance lock
    string StateFilePath)       // persisted env-failure / cooldown state
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    // ── Deploy worker (PublishManager) paths — repo-relative, defaults match
    //    infra/azure/00-config.sh. Override in config JSON only if the layout
    //    differs. Resolved against RepoPath at use time. ───────────────────────
    public string BpmSvcProj { get; init; } = "bpm-svc/src/Api/Api.csproj";
    public string AdminSvcProj { get; init; } = "bpm-admin-svc/src/Bpm.Admin.Api/Bpm.Admin.Api.csproj";
    public string BpmUiDir { get; init; } = "bpm-ui";
    public string AdminUiDir { get; init; } = "bpm-admin-ui";

    public static AgentConfig Load(string path) =>
        JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), Opts)
        ?? throw new InvalidOperationException($"empty or invalid config: {path}");

    public IEnumerable<EnvTarget> EnabledEnvironments => Environments.Where(e => e.Enabled);
}

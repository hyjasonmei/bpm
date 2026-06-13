using System.Text.Json;

namespace Bpm.ChefAgent;

/// <summary>
/// Owns the per-cook git worktree lifecycle. Branch names carry the env
/// prefix so the same flow cooked against local and azure never collide on
/// one repo. Naming + parsing are pure (unit-tested); the git calls are thin.
/// </summary>
public sealed class WorktreeManager
{
    private readonly AgentConfig _cfg;
    public WorktreeManager(AgentConfig cfg) => _cfg = cfg;

    /// <summary>cook/&lt;env&gt;/&lt;code-lower&gt;-v&lt;n&gt; — slashes are legal in git
    /// branch names and group cook branches under one ref namespace.</summary>
    public static string BranchName(string env, string flowCode, int version)
        => $"cook/{Sanitize(env)}/{Sanitize(flowCode).ToLowerInvariant()}-v{version}";

    /// <summary>&lt;worktreeRoot&gt;/&lt;env&gt;-&lt;code-lower&gt;-v&lt;n&gt; — a flat dir name
    /// (no slashes) so worktrees sit side by side under one root.</summary>
    public string WorktreePath(string env, string flowCode, int version)
        => Path.Combine(_cfg.WorktreeRoot, $"{Sanitize(env)}-{Sanitize(flowCode).ToLowerInvariant()}-v{version}");

    /// <summary>Read chef's recorded branch from a flow's ChefWorkContextJson
    /// (set via the chef_set_worktree MCP tool). Null when absent/garbage.</summary>
    public static string? BranchFromWorkContext(string? chefWorkContextJson)
    {
        if (string.IsNullOrWhiteSpace(chefWorkContextJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(chefWorkContextJson);
            return doc.RootElement.TryGetProperty("branch", out var b) ? b.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<ProcessResult> Git(IEnumerable<string> args, CancellationToken ct)
        => await ProcessRunner.RunAsync("git", args, _cfg.RepoPath, ct: ct);

    /// <summary>Create a fresh worktree + branch off main for a brand-new cook.</summary>
    public async Task<string> CreateAsync(string env, string flowCode, int version, CancellationToken ct = default)
    {
        var branch = BranchName(env, flowCode, version);
        var path = WorktreePath(env, flowCode, version);
        await Git(["worktree", "add", path, "-b", branch, "main"], ct);
        return path;
    }

    /// <summary>Count of changed/new files in the worktree — a coarse "work
    /// done" signal the cook loop uses to decide whether a session that ran
    /// out of turns actually made progress (resume) or stalled (give up).</summary>
    public async Task<int> MeasureProgressAsync(string worktreePath, CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync("git", ["status", "--porcelain"], worktreePath, ct: ct);
        return r.Ok ? r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;
    }

    /// <summary>Re-attach (or create) the worktree for a resume/stalled cook.</summary>
    public async Task<string> EnsureForBranchAsync(string env, string flowCode, int version, string branch, CancellationToken ct = default)
    {
        var path = WorktreePath(env, flowCode, version);
        if (Directory.Exists(path)) return path;
        await Git(["worktree", "add", path, branch], ct);
        return path;
    }

    /// <summary>Remove the worktree + delete the branch after a confirmed merge.</summary>
    public async Task CleanupAsync(string env, string flowCode, int version, string branch, CancellationToken ct = default)
    {
        var path = WorktreePath(env, flowCode, version);
        await Git(["worktree", "remove", path, "--force"], ct);
        await Git(["branch", "-D", branch], ct);
    }

    /// <summary>Write the per-worktree MCP config so the chef session inside it
    /// connects back to the right environment with the right token.</summary>
    public static async Task WriteMcpConfigAsync(string worktreePath, EnvTarget env, CancellationToken ct = default)
    {
        var mcp = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["flowcook-admin"] = new
                {
                    type = "sse",
                    url = env.BaseUrl.TrimEnd('/') + "/mcp",
                    headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {env.ChefToken}" },
                },
            },
        };
        await File.WriteAllTextAsync(
            Path.Combine(worktreePath, ".mcp.json"),
            JsonSerializer.Serialize(mcp, new JsonSerializerOptions { WriteIndented = true }), ct);
    }

    private static string Sanitize(string s) =>
        new(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
}

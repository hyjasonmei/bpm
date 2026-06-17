namespace Bpm.ChefAgent;

/// <summary>
/// Handles the Approved → merged → publishable tail for one environment:
/// opens a PR (or reminds when there's no remote), then detects the merge
/// and stamps MergedAt server-side. All git/gh interaction is local-only and
/// deterministic-state-driven; the cooldown decisions are pure (testable).
/// </summary>
public sealed class PrManager
{
    public static readonly TimeSpan RemindCooldown = TimeSpan.FromHours(24);

    private readonly AgentConfig _cfg;
    private readonly WorktreeManager _worktrees;
    private readonly TelegramNotifier _tg;
    private readonly AgentState _state;
    private readonly DateTime _now;

    public PrManager(AgentConfig cfg, WorktreeManager worktrees, TelegramNotifier tg, AgentState state, DateTime now)
    {
        _cfg = cfg; _worktrees = worktrees; _tg = tg; _state = state; _now = now;
    }

    private Task<ProcessResult> Git(params string[] args) => ProcessRunner.RunAsync("git", args, _cfg.RepoPath);
    private Task<ProcessResult> Gh(params string[] args) => ProcessRunner.RunAsync("gh", args, _cfg.RepoPath);

    /// <summary>True when the repo has a usable GitHub remote (origin/github).</summary>
    public async Task<bool> HasRemoteAsync()
    {
        var r = await Git("remote");
        return r.Ok && (r.Stdout.Contains("origin") || r.Stdout.Contains("github"));
    }

    /// <summary>Process one Approved-awaiting-merge flow for one environment.</summary>
    public async Task ProcessAsync(EnvTarget env, AdminApiClient api, ChefTask task, CancellationToken ct = default)
    {
        var branch = WorktreeManager.BranchName(env.Name, task.FlowCode, task.Version);
        var hasRemote = await HasRemoteAsync();

        // ── No PR yet: open one, or (no remote) remind to merge manually ──
        if (string.IsNullOrEmpty(task.PrUrl))
        {
            if (!hasRemote)
            {
                // Local mode: if the cook branch fast-forwards cleanly into main,
                // the agent merges it itself (--ff-only, never a merge commit /
                // conflict resolution). Otherwise fall back to the manual reminder.
                if (await TryAutoFfMergeAsync(branch, api, task, ct)) return;

                if (Cooldown.ShouldSend(_now, Lookup(_state.LastRemindedAt, task.FlowId), RemindCooldown))
                {
                    await api.PostMemoAsync(task.FlowId, $"Branch `{branch}` is ready — merge it into main manually, then press Publish (or Mark merged).", ct);
                    await _tg.SendAsync($"📝 {task.FlowCode} v{task.Version} cooked & approved. No remote → merge `{branch}` into main manually, then Publish.", ct);
                    _state.LastRemindedAt[task.FlowId.ToString()] = _now;
                }
                return;
            }

            var body = BuildPrBody(task, branch);
            var bodyFile = Path.Combine(Path.GetTempPath(), $"chef-pr-{task.FlowId:N}.md");
            await File.WriteAllTextAsync(bodyFile, body, ct);
            var create = await Gh("pr", "create", "--head", branch,
                "--title", $"cook: {task.FlowCode} v{task.Version} — {task.DisplayName}",
                "--body-file", bodyFile);
            try { File.Delete(bodyFile); } catch { /* best effort */ }

            if (create.Ok)
            {
                var url = create.Stdout.Trim().Split('\n').LastOrDefault(l => l.Contains("http"))?.Trim();
                if (!string.IsNullOrEmpty(url))
                {
                    await api.SetPrUrlAsync(task.FlowId, url, ct);
                    await _tg.SendAsync($"🔀 PR opened for {task.FlowCode} v{task.Version}: {url}", ct);
                }
            }
            else
            {
                await _tg.SendAsync($"⚠️ gh pr create failed for {task.FlowCode} v{task.Version}: {create.Stderr.Trim()}", ct);
            }
            return;
        }

        // ── PR exists (or remote-less but branch tracked): detect merge ──
        var merged = task.PrUrl is { Length: > 0 } prUrl
            ? await IsPrMergedAsync(prUrl)
            : await IsAncestorOfMainAsync(branch);

        if (merged)
        {
            await api.MarkMergedAsync(task.FlowId, task.PrUrl is { Length: > 0 } ? "gh-pr" : "git-ancestry", ct);
            await TryCleanupAsync(env, task, branch, ct);
            await _tg.SendAsync($"✅ {task.FlowCode} v{task.Version} merged to main — ready to Publish.", ct);
        }
    }

    /// <summary>gh reports a non-null mergedAt once the PR is merged (works for
    /// squash/rebase merges too, unlike branch ancestry).</summary>
    public async Task<bool> IsPrMergedAsync(string prUrl)
    {
        var r = await Gh("pr", "view", prUrl, "--json", "mergedAt", "-q", ".mergedAt");
        return r.Ok && !string.IsNullOrWhiteSpace(r.Stdout) && !r.Stdout.Contains("null");
    }

    /// <summary>Remote-less fallback: the branch tip is an ancestor of main.
    /// (Breaks on squash merge — the admin "Mark merged" button covers that.)</summary>
    public async Task<bool> IsAncestorOfMainAsync(string branch)
    {
        var r = await Git("merge-base", "--is-ancestor", branch, "main");
        return r.ExitCode == 0;
    }

    /// <summary>Auto-ff-merge gate (pure): only when the cook branch is ahead of
    /// main AND a fast-forward is possible. Never auto-creates a merge commit or
    /// resolves conflicts — those fall back to the manual-merge reminder.</summary>
    public static bool ShouldAutoFfMerge(bool branchAheadOfMain, bool ffPossible)
        => branchAheadOfMain && ffPossible;

    /// <summary>Local-mode auto-merge: ff-only merge the cook branch into main and
    /// stamp MergedAt server-side. Returns true when it merged (caller stops),
    /// false to fall through to the manual reminder.</summary>
    private async Task<bool> TryAutoFfMergeAsync(string branch, AdminApiClient api, ChefTask task, CancellationToken ct)
    {
        // branch ahead of main: main is an ancestor of the branch tip.
        var mainAncestorOfBranch = (await Git("merge-base", "--is-ancestor", "main", branch)).ExitCode == 0;
        // ff possible (no divergence): branch contains main's tip, i.e. nothing on
        // main that the branch lacks — a `merge --ff-only` can succeed.
        var branchAheadOfMain = mainAncestorOfBranch
            && (await Git("merge-base", "--is-ancestor", branch, "main")).ExitCode != 0; // strictly ahead
        var ffPossible = mainAncestorOfBranch; // main reachable from branch ⇒ ff-able

        if (!ShouldAutoFfMerge(branchAheadOfMain, ffPossible)) return false;

        var checkout = await Git("checkout", "main");
        if (!checkout.Ok)
        {
            await _tg.SendAsync($"⚠️ auto-ff-merge: `git checkout main` failed for {task.FlowCode} v{task.Version}: {checkout.Stderr.Trim()}", ct);
            return false;
        }
        var merge = await Git("merge", "--ff-only", branch);
        if (!merge.Ok)
        {
            // non-ff / conflict / race → leave it for the manual reminder path.
            await _tg.SendAsync($"⚠️ auto-ff-merge: `git merge --ff-only {branch}` failed for {task.FlowCode} v{task.Version} — falling back to manual merge.", ct);
            return false;
        }

        await api.MarkMergedAsync(task.FlowId, "agent-ff-merge", ct);
        await _tg.SendAsync($"✅ {task.FlowCode} v{task.Version} merged to main (agent ff) — ready to Publish.", ct);
        return true;
    }

    private async Task TryCleanupAsync(EnvTarget env, ChefTask task, string branch, CancellationToken ct)
    {
        try { await _worktrees.CleanupAsync(env.Name, task.FlowCode, task.Version, branch, ct); }
        catch (Exception ex) { Console.Error.WriteLine($"[worktree-cleanup-fail] {branch}: {ex.Message}"); }
    }

    /// <summary>PR description — pure so it's unit-tested.</summary>
    public static string BuildPrBody(ChefTask task, string branch) => $"""
        Auto-opened by chef-agent for the cooked flow **{task.FlowCode} v{task.Version}** ({task.DisplayName}).

        - Branch: `{branch}`
        - Flow id: `{task.FlowId}`

        Review the per-flow Features diff against the Clean Architecture layer map,
        then merge. Publish unlocks in admin-ui once the merge is detected.

        > ⚠️ If you **squash** merge, branch-ancestry detection can't see it in a
        > remote-less setup — use the admin **Mark merged** button if Publish stays locked.

        🤖 Generated with chef-agent
        """;

    private static DateTime? Lookup(Dictionary<string, DateTime> map, Guid flowId)
        => map.TryGetValue(flowId.ToString(), out var v) ? v : null;
}

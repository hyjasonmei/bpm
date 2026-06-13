namespace Bpm.ChefAgent;

public enum CookOutcome { Committed, OnHold, Incomplete, FlowGone }

/// <summary>
/// Spins a headless `claude -p` session inside the cook worktree, then reads
/// the resulting flow state back from the API to classify the outcome.
/// </summary>
public sealed class CookRunner
{
    private readonly AgentConfig _cfg;
    private readonly WorktreeManager _worktrees;
    public CookRunner(AgentConfig cfg, WorktreeManager worktrees) { _cfg = cfg; _worktrees = worktrees; }

    /// <summary>The cook instruction handed to claude. Pure so it's unit-tested.</summary>
    public static string BuildPrompt(ChefTask task, bool isResume)
    {
        var verb = isResume
            ? "Resume cooking (a prior session paused or crashed). FIRST call chef_get_messages and read the FULL thread — the user may have replied or filed an issue you must address."
            : "Cook this newly submitted flow from scratch.";
        return $"""
        You are the flowcook chef. Use the chef-codegen skill to cook flow {task.FlowCode} v{task.Version} (flowId={task.FlowId}).
        {verb}

        Steps:
        1. chef_get_flow to read the spec; chef_get_messages for the conversation.
        2. chef_download_bundle and unzip into this worktree.
        3. Develop strictly per the chef skill's Clean Architecture layer map.
        4. Run the bpm-svc tests; only when green, chef_transition to Committed and
           chef_post_message a Completion (kind=Completion) summarising the cook.
        If you hit anything that needs a human decision, chef_transition to OnHold
        with a clear question, then stop. You are already on the correct worktree
        and branch — do NOT switch branches.
        """;
    }

    public async Task<CookOutcome> RunAsync(EnvTarget env, AdminApiClient api, ChefTask task, bool isResume, CancellationToken ct = default)
    {
        // Prepare the worktree: fresh branch for a new cook, re-attach chef's
        // recorded branch for a resume/stalled one.
        string worktree;
        if (isResume && WorktreeManager.BranchFromWorkContext(task.ChefWorkContextJson) is { } branch)
            worktree = await _worktrees.EnsureForBranchAsync(env.Name, task.FlowCode, task.Version, branch, ct);
        else
            worktree = await _worktrees.CreateAsync(env.Name, task.FlowCode, task.Version, ct);

        await WorktreeManager.WriteMcpConfigAsync(worktree, env, ct);

        var result = await ProcessRunner.RunAsync(
            _cfg.ClaudeBin,
            ["-p", BuildPrompt(task, isResume),
             "--max-turns", _cfg.MaxTurns.ToString(),
             "--permission-mode", "bypassPermissions"],
            workingDir: worktree,
            timeout: TimeSpan.FromMinutes(_cfg.MaxSessionMinutes),
            ct: ct);

        // The session's own exit code is advisory; the flow's state is the
        // source of truth for what actually happened.
        var state = await api.GetStateAsync(task.FlowId, ct);
        return state switch
        {
            null        => CookOutcome.FlowGone,    // deleted mid-cook (pre-publish delete is allowed)
            "Committed" => CookOutcome.Committed,
            "OnHold"    => CookOutcome.OnHold,
            _           => CookOutcome.Incomplete,  // still Cooking / unexpected → stall policy
        };
    }
}

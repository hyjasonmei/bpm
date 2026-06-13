namespace Bpm.ChefAgent;

public enum CookOutcome { Committed, OnHold, GaveUp, FlowGone }

/// <summary>
/// Drives a flow to completion across one or more headless `claude -p`
/// sessions in the cook worktree. A per-session turn/wall-clock cap is just a
/// chunk size: if a session ends with the flow still Cooking but made progress
/// (more changed files in the worktree), we resume and continue; we only give
/// up — moving the flow to OnHold for a human — when a session produces nothing.
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
            ? "Resume cooking (a prior session paused, ran out of turns, or crashed). FIRST call chef_get_messages and read the FULL thread, and inspect the worktree for what's already done — continue from there, don't restart. The user may also have replied or filed an issue you must address."
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
        // Prepare the worktree ONCE: fresh branch for a new cook, re-attach
        // chef's recorded branch for a resume/stalled one.
        string worktree;
        if (isResume && WorktreeManager.BranchFromWorkContext(task.ChefWorkContextJson) is { } branch)
            worktree = await _worktrees.EnsureForBranchAsync(env.Name, task.FlowCode, task.Version, branch, ct);
        else
            worktree = await _worktrees.CreateAsync(env.Name, task.FlowCode, task.Version, ct);
        await WorktreeManager.WriteMcpConfigAsync(worktree, env, ct);

        var prevProgress = await _worktrees.MeasureProgressAsync(worktree, ct);
        var noProgressStreak = 0;
        var sessionsRun = 0;
        var resuming = isResume;

        while (true)
        {
            await ProcessRunner.RunAsync(
                _cfg.ClaudeBin,
                ["-p", BuildPrompt(task, resuming),
                 "--max-turns", _cfg.MaxTurns.ToString(),
                 "--permission-mode", "bypassPermissions",
                 // Load ONLY the flowcook-admin chef MCP from the worktree config.
                 // Without --mcp-config the project .mcp.json isn't trusted in
                 // headless mode (chef tools missing); --strict-mcp-config stops
                 // the session inheriting the operator's user-level MCP servers
                 // (e.g. telegram), which otherwise get hijacked from the parent.
                 "--mcp-config", Path.Combine(worktree, ".mcp.json"),
                 "--strict-mcp-config"],
                workingDir: worktree,
                timeout: TimeSpan.FromMinutes(_cfg.MaxSessionMinutes),
                ct: ct);
            sessionsRun++;

            // Flow state is the source of truth for what happened; worktree
            // file-churn is the progress signal for the resume decision.
            var state = await api.GetStateAsync(task.FlowId, ct);
            var curProgress = await _worktrees.MeasureProgressAsync(worktree, ct);
            var decision = CookLoopPolicy.Decide(
                state, prevProgress, curProgress,
                noProgressStreak, sessionsRun, _cfg.MaxNoProgressSessions, _cfg.MaxCookSessions);

            Console.WriteLine(
                $"[cook] {task.FlowCode} session {sessionsRun}: state={state ?? "?"}, " +
                $"progress {prevProgress}→{curProgress} → {decision.Action} ({decision.Reason})");

            switch (decision.Action)
            {
                case CookLoopAction.Done:
                    return state switch
                    {
                        null        => CookOutcome.FlowGone,
                        "Committed" => CookOutcome.Committed,
                        _           => CookOutcome.OnHold,   // chef asked a question
                    };

                case CookLoopAction.GiveUp:
                    await api.OnHoldAsync(task.FlowId,
                        $"Automated cook stopped — {decision.Reason} ({sessionsRun} session(s)). Please review the worktree / spec and reply to resume.", ct);
                    return CookOutcome.GaveUp;

                case CookLoopAction.Resume:
                    noProgressStreak = curProgress > prevProgress ? 0 : noProgressStreak + 1;
                    prevProgress = curProgress;
                    resuming = true;
                    continue;
            }
        }
    }
}

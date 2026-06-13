namespace Bpm.ChefAgent;

public enum CookLoopAction { Resume, Done, GiveUp }

public sealed record CookDecision(CookLoopAction Action, string Reason);

/// <summary>
/// Pure decision for the in-run cook loop: after one chef session ends, do we
/// resume (the session ran out of turns / wall-clock but made progress),
/// stop (terminal state reached), or give up (genuinely stuck — no progress)?
///
/// The whole point: a per-session turn cap is just a chunk size, NOT a
/// "must finish in one go" limit. A complex flow spans several sessions, each
/// resuming from the worktree + chat history. We keep going while progress is
/// being made and only surface to a human when a session produces nothing.
/// </summary>
public static class CookLoopPolicy
{
    public static CookDecision Decide(
        string? state,            // flow state after the session
        int prevProgress,         // progress signal before this session
        int curProgress,          // progress signal after this session
        int noProgressStreak,     // consecutive no-progress sessions BEFORE this one
        int sessionsRun,          // sessions spawned so far this run (>= 1)
        int maxNoProgressSessions,
        int maxCookSessions)
    {
        if (state is null) return new(CookLoopAction.Done, "flow deleted mid-cook");
        if (state == "Committed") return new(CookLoopAction.Done, "cook committed");
        if (state == "OnHold") return new(CookLoopAction.Done, "chef asked a question (on hold)");
        if (state != "Cooking") return new(CookLoopAction.Done, $"unexpected terminal state: {state}");

        // Still Cooking → the session ended without finishing (turns / timeout / crash).
        if (sessionsRun >= maxCookSessions)
            return new(CookLoopAction.GiveUp, $"hit absolute session cap ({maxCookSessions}) — needs a human");

        if (curProgress > prevProgress)
            return new(CookLoopAction.Resume, "made progress — resuming to continue");

        // No progress this session.
        if (noProgressStreak + 1 >= maxNoProgressSessions)
            return new(CookLoopAction.GiveUp, $"no progress for {maxNoProgressSessions} session(s) — chef appears stuck");

        return new(CookLoopAction.Resume, "no progress yet — one more resume before giving up");
    }
}

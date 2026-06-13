namespace Bpm.ChefAgent;

/// <summary>
/// Pure decision logic: given one environment's task list, pick at most ONE
/// cook task for this poll and the set of merge checks to run. Priority — a
/// stalled cook (a crashed agent left it Cooking with a cold heartbeat), then
/// a user-answered hold, then a fresh submission. Within-run resume is handled
/// by the cook loop; a cook that genuinely can't finish is parked on OnHold,
/// so no per-poll retry counter is needed here. Approved-awaiting-merge never
/// occupies the single cook slot (cheap, no LLM), so they all flow each poll.
/// </summary>
public static class TaskPlanner
{
    public static CookPlan Plan(ChefTaskList tasks)
    {
        // Stalled = a previous agent run died mid-cook (Cooking + cold heartbeat).
        // Resume it in place (already Cooking — don't re-claim).
        var stalled = tasks.Stalled.OrderBy(t => t.UpdatedAt).FirstOrDefault();
        if (stalled is not null)
            return new CookPlan(stalled, IsResume: true, tasks.ApprovedAwaitingMerge);

        // User answered a hold → highest live-work priority (someone is waiting).
        var awaiting = tasks.AwaitingChef.OrderBy(t => t.UpdatedAt).FirstOrDefault();
        if (awaiting is not null)
            return new CookPlan(awaiting, IsResume: true, tasks.ApprovedAwaitingMerge);

        // Fresh submission → brand-new cook (will be claimed Submitted→Cooking).
        var submitted = tasks.Submitted.OrderBy(t => t.UpdatedAt).FirstOrDefault();
        if (submitted is not null)
            return new CookPlan(submitted, IsResume: false, tasks.ApprovedAwaitingMerge);

        return CookPlan.NothingToCook(tasks.ApprovedAwaitingMerge);
    }
}

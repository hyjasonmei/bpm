namespace Bpm.ChefAgent;

/// <summary>
/// Pure decision logic: given one environment's task list (plus the persisted
/// retry counts), pick at most ONE cook task for this poll and the set of
/// merge checks to run. Priority — a crashed cook we can still retry, then a
/// user-answered hold, then a fresh submission. Approved-awaiting-merge never
/// occupies the single cook slot (the checks are cheap, no LLM), so they all
/// flow through every poll.
/// </summary>
public static class TaskPlanner
{
    public static CookPlan Plan(ChefTaskList tasks, IReadOnlyDictionary<string, int> retries, int maxAutoRetries)
    {
        // Retryable stalled cook: a crashed session whose auto-retry budget
        // isn't spent. These resume in place (already Cooking — don't re-claim).
        var retryableStalled = tasks.Stalled
            .Where(t => Tries(retries, t.FlowId) < maxAutoRetries)
            .OrderBy(t => t.UpdatedAt)
            .FirstOrDefault();
        if (retryableStalled is not null)
            return new CookPlan(retryableStalled, IsResume: true, tasks.ApprovedAwaitingMerge);

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

    private static int Tries(IReadOnlyDictionary<string, int> retries, Guid flowId)
        => retries.TryGetValue(flowId.ToString(), out var n) ? n : 0;
}

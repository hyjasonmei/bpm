using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class TaskPlannerTests
{
    private static ChefTask T(string code, int minutesAgo = 0) => new(
        Guid.NewGuid(), code, 1, code, "x",
        DateTime.UtcNow.AddMinutes(-minutesAgo), null, null, null);

    private static readonly Dictionary<string, int> NoRetries = new();

    [Fact]
    public void Picks_AwaitingChef_before_Submitted()
    {
        var tasks = new ChefTaskList([T("PUR")], [T("LEAVE")], [], []);
        var plan = TaskPlanner.Plan(tasks, NoRetries, maxAutoRetries: 1);
        Assert.Equal("LEAVE", plan.CookTask!.FlowCode);
        Assert.True(plan.IsResume);
    }

    [Fact]
    public void Picks_Submitted_when_no_awaiting()
    {
        var tasks = new ChefTaskList([T("PUR")], [], [], []);
        var plan = TaskPlanner.Plan(tasks, NoRetries, maxAutoRetries: 1);
        Assert.Equal("PUR", plan.CookTask!.FlowCode);
        Assert.False(plan.IsResume);   // fresh cook → will be claimed
    }

    [Fact]
    public void Retryable_stalled_beats_everything()
    {
        var tasks = new ChefTaskList([T("PUR")], [T("LEAVE")], [], [T("OLD")]);
        var plan = TaskPlanner.Plan(tasks, NoRetries, maxAutoRetries: 1);
        Assert.Equal("OLD", plan.CookTask!.FlowCode);
        Assert.True(plan.IsResume);
    }

    [Fact]
    public void Stalled_past_retry_budget_is_skipped()
    {
        var stalled = T("OLD");
        var tasks = new ChefTaskList([], [T("LEAVE")], [], [stalled]);
        var retries = new Dictionary<string, int> { [stalled.FlowId.ToString()] = 1 };
        var plan = TaskPlanner.Plan(tasks, retries, maxAutoRetries: 1);
        Assert.Equal("LEAVE", plan.CookTask!.FlowCode);   // falls through to awaiting
    }

    [Fact]
    public void Cook_is_null_when_queue_empty_but_merge_checks_flow_through()
    {
        var tasks = new ChefTaskList([], [], [T("APE")], []);
        var plan = TaskPlanner.Plan(tasks, NoRetries, maxAutoRetries: 1);
        Assert.Null(plan.CookTask);
        Assert.Single(plan.MergeChecks);
    }

    [Fact]
    public void Oldest_submitted_wins()
    {
        var newer = T("NEW", minutesAgo: 1);
        var older = T("OLD", minutesAgo: 30);
        var tasks = new ChefTaskList([newer, older], [], [], []);
        var plan = TaskPlanner.Plan(tasks, NoRetries, maxAutoRetries: 1);
        Assert.Equal("OLD", plan.CookTask!.FlowCode);
    }
}

using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class TaskPlannerTests
{
    private static ChefTask T(string code, int minutesAgo = 0) => new(
        Guid.NewGuid(), code, 1, code, "x",
        DateTime.UtcNow.AddMinutes(-minutesAgo), null, null, null);

    [Fact]
    public void Picks_AwaitingChef_before_Submitted()
    {
        var tasks = new ChefTaskList([T("PUR")], [T("LEAVE")], [], []);
        var plan = TaskPlanner.Plan(tasks);
        Assert.Equal("LEAVE", plan.CookTask!.FlowCode);
        Assert.True(plan.IsResume);
    }

    [Fact]
    public void Picks_Submitted_when_no_awaiting()
    {
        var tasks = new ChefTaskList([T("PUR")], [], [], []);
        var plan = TaskPlanner.Plan(tasks);
        Assert.Equal("PUR", plan.CookTask!.FlowCode);
        Assert.False(plan.IsResume);   // fresh cook → will be claimed
    }

    [Fact]
    public void Stalled_beats_everything_and_resumes()
    {
        var tasks = new ChefTaskList([T("PUR")], [T("LEAVE")], [], [T("OLD")]);
        var plan = TaskPlanner.Plan(tasks);
        Assert.Equal("OLD", plan.CookTask!.FlowCode);
        Assert.True(plan.IsResume);
    }

    [Fact]
    public void Cook_is_null_when_queue_empty_but_merge_checks_flow_through()
    {
        var tasks = new ChefTaskList([], [], [T("APE")], []);
        var plan = TaskPlanner.Plan(tasks);
        Assert.Null(plan.CookTask);
        Assert.Single(plan.MergeChecks);
    }

    [Fact]
    public void Oldest_submitted_wins()
    {
        var newer = T("NEW", minutesAgo: 1);
        var older = T("OLD", minutesAgo: 30);
        var tasks = new ChefTaskList([newer, older], [], [], []);
        var plan = TaskPlanner.Plan(tasks);
        Assert.Equal("OLD", plan.CookTask!.FlowCode);
    }
}

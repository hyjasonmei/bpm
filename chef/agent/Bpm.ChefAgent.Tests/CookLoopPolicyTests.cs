using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class CookLoopPolicyTests
{
    // maxNoProgress=2, maxSessions=8 unless overridden.
    private static CookDecision Decide(string? state, int prev, int cur,
        int noProgressStreak = 0, int sessionsRun = 1, int maxNoProgress = 2, int maxSessions = 8)
        => CookLoopPolicy.Decide(state, prev, cur, noProgressStreak, sessionsRun, maxNoProgress, maxSessions);

    [Fact]
    public void Committed_is_done()
        => Assert.Equal(CookLoopAction.Done, Decide("Committed", 0, 10).Action);

    [Fact]
    public void OnHold_is_done()
        => Assert.Equal(CookLoopAction.Done, Decide("OnHold", 5, 5).Action);

    [Fact]
    public void Deleted_flow_is_done()
        => Assert.Equal(CookLoopAction.Done, Decide(null, 5, 5).Action);

    [Fact]
    public void Cooking_with_progress_resumes()
    {
        var d = Decide("Cooking", prev: 4, cur: 11);
        Assert.Equal(CookLoopAction.Resume, d.Action);
    }

    [Fact]
    public void Cooking_no_progress_first_time_resumes_once_more()
    {
        var d = Decide("Cooking", prev: 11, cur: 11, noProgressStreak: 0, maxNoProgress: 2);
        Assert.Equal(CookLoopAction.Resume, d.Action);
    }

    [Fact]
    public void Cooking_no_progress_hitting_streak_gives_up()
    {
        // one prior no-progress session + this one = 2 = maxNoProgress → give up
        var d = Decide("Cooking", prev: 11, cur: 11, noProgressStreak: 1, maxNoProgress: 2);
        Assert.Equal(CookLoopAction.GiveUp, d.Action);
    }

    [Fact]
    public void Absolute_session_cap_gives_up_even_with_progress()
    {
        var d = Decide("Cooking", prev: 4, cur: 20, sessionsRun: 8, maxSessions: 8);
        Assert.Equal(CookLoopAction.GiveUp, d.Action);
    }
}

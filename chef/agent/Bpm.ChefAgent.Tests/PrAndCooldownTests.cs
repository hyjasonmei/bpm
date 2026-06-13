using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class PrAndCooldownTests
{
    private static readonly DateTime Now = new(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cooldown_sends_when_never_sent()
        => Assert.True(Cooldown.ShouldSend(Now, null, TimeSpan.FromHours(1)));

    [Fact]
    public void Cooldown_suppresses_within_window()
        => Assert.False(Cooldown.ShouldSend(Now, Now.AddMinutes(-30), TimeSpan.FromHours(1)));

    [Fact]
    public void Cooldown_sends_after_window()
        => Assert.True(Cooldown.ShouldSend(Now, Now.AddMinutes(-90), TimeSpan.FromHours(1)));

    [Fact]
    public void Cooldown_sends_exactly_at_boundary()
        => Assert.True(Cooldown.ShouldSend(Now, Now.AddHours(-1), TimeSpan.FromHours(1)));

    [Fact]
    public void PrBody_mentions_flow_branch_and_squash_warning()
    {
        var task = new ChefTask(Guid.NewGuid(), "LEAVE", 1, "請假", "Approved",
            DateTime.UtcNow, null, null, null);
        var body = PrManager.BuildPrBody(task, "cook/local/leave-v1");

        Assert.Contains("LEAVE v1", body);
        Assert.Contains("cook/local/leave-v1", body);
        Assert.Contains(task.FlowId.ToString(), body);
        Assert.Contains("Mark merged", body);   // squash escape-hatch reminder
    }
}

using Bpm.ChefAgent;

namespace Bpm.ChefAgent.Tests;

public class PublishManagerTests
{
    private static readonly DateTime Now = new(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Starts_when_no_prior_attempt_and_not_in_flight()
        => Assert.True(PublishManager.ShouldStartDeploy(Now, lastAttemptAt: null, inFlight: false));

    [Fact]
    public void Does_not_start_when_in_flight()
        => Assert.False(PublishManager.ShouldStartDeploy(Now, lastAttemptAt: null, inFlight: true));

    [Fact]
    public void Does_not_start_within_retry_cooldown()
        => Assert.False(PublishManager.ShouldStartDeploy(Now, Now.AddMinutes(-10), inFlight: false));

    [Fact]
    public void Starts_after_retry_cooldown_elapses()
        => Assert.True(PublishManager.ShouldStartDeploy(Now, Now.AddMinutes(-31), inFlight: false));

    [Fact]
    public void Starts_exactly_at_retry_cooldown_boundary()
        => Assert.True(PublishManager.ShouldStartDeploy(Now, Now.Subtract(PublishManager.RetryCooldown), inFlight: false));

    [Fact]
    public void In_flight_blocks_even_after_cooldown()
        => Assert.False(PublishManager.ShouldStartDeploy(Now, Now.AddHours(-2), inFlight: true));
}

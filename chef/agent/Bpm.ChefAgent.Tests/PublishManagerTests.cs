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

    [Fact]
    public void ParseBundleId_extracts_main_bundle_from_index_html()
    {
        const string html =
            "<!doctype html><html><head>" +
            "<script type=\"module\" crossorigin src=\"/assets/index-CvkIajz5.js\"></script>" +
            "<link rel=\"stylesheet\" href=\"/assets/index-zP3A3g2w.css\">" +
            "</head><body></body></html>";
        Assert.Equal("/assets/index-CvkIajz5.js", PublishManager.ParseBundleId(html));
    }

    [Fact]
    public void ParseBundleId_returns_null_when_no_bundle_reference()
        => Assert.Null(PublishManager.ParseBundleId("<html><head></head><body>no assets here</body></html>"));

    [Fact]
    public void ShouldRetryMark_retries_on_timeout_when_attempts_remain()
        => Assert.True(PublishManager.ShouldRetryMark(attempt: 1, maxAttempts: 5, new TaskCanceledException(), shuttingDown: false));

    [Fact]
    public void ShouldRetryMark_retries_on_transport_error()
        => Assert.True(PublishManager.ShouldRetryMark(attempt: 2, maxAttempts: 5, new HttpRequestException("connection refused"), shuttingDown: false));

    [Fact]
    public void ShouldRetryMark_stops_on_last_attempt()
        => Assert.False(PublishManager.ShouldRetryMark(attempt: 5, maxAttempts: 5, new TaskCanceledException(), shuttingDown: false));

    [Fact]
    public void ShouldRetryMark_does_not_retry_when_shutting_down()
        => Assert.False(PublishManager.ShouldRetryMark(attempt: 1, maxAttempts: 5, new TaskCanceledException(), shuttingDown: true));

    [Fact]
    public void ShouldRetryMark_does_not_retry_non_transient_error()
        => Assert.False(PublishManager.ShouldRetryMark(attempt: 1, maxAttempts: 5, new InvalidOperationException("409"), shuttingDown: false));

    [Fact]
    public void FrontendApiMarker_builds_slash_api_slug_version()
        => Assert.Equal("/api/wfh/v3", PublishManager.FrontendApiMarker("WFH", 3));

    [Fact]
    public void FrontendApiMarker_hyphenates_underscored_flow_codes()
        => Assert.Equal("/api/purchase-request/v1", PublishManager.FrontendApiMarker("PURCHASE_REQUEST", 1));
}

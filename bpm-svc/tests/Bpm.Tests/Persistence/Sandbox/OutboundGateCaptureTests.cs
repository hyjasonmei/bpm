using System.Text.Json;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Sandbox;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Sandbox;

/// <summary>
/// Behavioural coverage for <see cref="OutboundGate"/>: when sandbox mode is on,
/// the only outcome is <c>Captured</c> (a row in <see cref="SandboxCapturedMessage"/>
/// with the full payload). The legacy "rewrite to alt recipients / drop" path
/// was removed in PR-J6 — capture-only is now the contract.
/// </summary>
public sealed class OutboundGateCaptureTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public OutboundGateCaptureTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewCtx() => new(_options);

    private async Task SetSandboxAsync(bool enabled, SandboxConfigDto? config = null)
    {
        await using var db = NewCtx();
        var existing = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == "default");
        if (existing is null)
        {
            existing = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(existing);
        }
        existing.SandboxMode = enabled;
        existing.SandboxConfigJson = config is null ? null : JsonSerializer.Serialize(config);
        await db.SaveChangesAsync();
    }

    private static EmailMessage SampleEmail(
        string subject = "[BPM] 請假申請已送出",
        string? originatingNotificationId = null,
        Guid? processInstanceId = null) => new(
        To: new[] { "wilson@example.com" },
        Cc: new[] { "manager@example.com" },
        Bcc: new[] { "hr@example.com" },
        Subject: subject,
        BodyHtml: "<p>請於明日前審核</p>",
        BodyText: "請於明日前審核",
        OriginatingNotificationId: originatingNotificationId,
        ProcessInstanceId: processInstanceId);

    private static WebhookDelivery SampleWebhook() => new(
        Url: "https://customer.example.com/hooks/bpm",
        Headers: new Dictionary<string, string>
        {
            ["X-BPM-Event"] = "leave.approved",
            ["Content-Type"] = "application/json",
        },
        PayloadJson: "{\"instanceId\":\"abc\",\"status\":\"approved\"}",
        EventType: "leave.approved",
        OriginatingWebhookSubscriptionId: "sub-42");

    private static SmsMessage SampleSms() => new(
        To: new[] { "+886912345678", "+886987654321" },
        Body: "您的請假已核准");

    [Fact]
    public async Task Sandbox_off_passes_through_email_without_capture()
    {
        await SetSandboxAsync(enabled: false);

        await using var db = NewCtx();
        var gate = new OutboundGate(db, _clock);
        var msg = SampleEmail();

        var outcome = await gate.ApplyAsync(msg);

        Assert.False(outcome.Captured);
        Assert.False(outcome.Rewritten);
        Assert.False(outcome.Dropped);
        Assert.Equal(msg, outcome.Message);
        Assert.Null(outcome.CapturedMessageId);

        await using var verify = NewCtx();
        Assert.False(await verify.SandboxCapturedMessages.AnyAsync());
    }

    [Fact]
    public async Task Sandbox_on_default_captures_email_with_full_payload()
    {
        await SetSandboxAsync(enabled: true, config: new SandboxConfigDto(
            EmailRecipients: null,
            WebhookUrl: null,
            SmsRecipients: null));

        await using var db = NewCtx();
        var gate = new OutboundGate(db, _clock);
        var msg = SampleEmail();

        var outcome = await gate.ApplyAsync(msg);

        Assert.True(outcome.Captured);
        Assert.False(outcome.Rewritten);
        Assert.False(outcome.Dropped);
        Assert.NotNull(outcome.CapturedMessageId);
        Assert.Null(outcome.Message); // Captured outcome carries no message — caller treats as success.

        await using var verify = NewCtx();
        var row = await verify.SandboxCapturedMessages.SingleAsync();
        Assert.Equal(outcome.CapturedMessageId, row.Id);
        Assert.Equal(SandboxChannel.Email, row.Channel);
        Assert.Equal(msg.Subject, row.Subject);
        Assert.Equal(msg.BodyHtml, row.BodyHtml);
        Assert.Equal(msg.BodyText, row.BodyText);
        var recipients = JsonSerializer.Deserialize<List<string>>(row.IntendedRecipientsJson)!;
        Assert.Equal(new[] { "wilson@example.com", "manager@example.com", "hr@example.com" }, recipients);
        Assert.Equal(_clock.UtcNow, row.CapturedAt);
    }

    [Fact]
    public async Task Sandbox_on_default_captures_webhook_with_url_headers_and_payload()
    {
        await SetSandboxAsync(enabled: true);

        await using var db = NewCtx();
        var gate = new OutboundGate(db, _clock);
        var msg = SampleWebhook();

        var outcome = await gate.ApplyAsync(msg);

        Assert.True(outcome.Captured);
        Assert.NotNull(outcome.CapturedMessageId);

        await using var verify = NewCtx();
        var row = await verify.SandboxCapturedMessages.SingleAsync();
        Assert.Equal(SandboxChannel.Webhook, row.Channel);
        Assert.Equal(msg.Url, row.Url);
        Assert.Equal(msg.PayloadJson, row.PayloadJson);
        Assert.Equal(msg.EventType, row.EventType);
        Assert.Equal("sub-42", row.OriginatingWebhookSubscriptionId);
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.HeadersJson!)!;
        Assert.Equal("leave.approved", headers["X-BPM-Event"]);
        Assert.Equal("application/json", headers["Content-Type"]);
        var recipients = JsonSerializer.Deserialize<List<string>>(row.IntendedRecipientsJson)!;
        Assert.Single(recipients);
        Assert.Equal(msg.Url, recipients[0]);
    }

    [Fact]
    public async Task Sandbox_on_default_captures_sms_with_body_and_intended_numbers()
    {
        await SetSandboxAsync(enabled: true);

        await using var db = NewCtx();
        var gate = new OutboundGate(db, _clock);
        var msg = SampleSms();

        var outcome = await gate.ApplyAsync(msg);

        Assert.True(outcome.Captured);

        await using var verify = NewCtx();
        var row = await verify.SandboxCapturedMessages.SingleAsync();
        Assert.Equal(SandboxChannel.Sms, row.Channel);
        Assert.Equal(msg.Body, row.Body);
        var recipients = JsonSerializer.Deserialize<List<string>>(row.IntendedRecipientsJson)!;
        Assert.Equal(new[] { "+886912345678", "+886987654321" }, recipients);
    }

    [Fact]
    public async Task Originating_notification_and_instance_round_trip_into_capture_row()
    {
        await SetSandboxAsync(enabled: true);

        var instanceId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        await using var db = NewCtx();
        var gate = new OutboundGate(db, _clock);
        var msg = SampleEmail(
            originatingNotificationId: "manager_review_requested",
            processInstanceId: instanceId) with
        {
            TaskId = taskId,
        };

        var outcome = await gate.ApplyAsync(msg);

        Assert.True(outcome.Captured);

        await using var verify = NewCtx();
        var row = await verify.SandboxCapturedMessages.SingleAsync();
        Assert.Equal("manager_review_requested", row.OriginatingNotificationId);
        Assert.Equal(instanceId, row.ProcessInstanceId);
        Assert.Equal(taskId, row.TaskId);
    }

    [Fact]
    public async Task GateOutcome_capture_factory_sets_flags_correctly()
    {
        // Guards against future regressions of the GateOutcome record shape —
        // the four factories MUST be mutually exclusive on (Dropped, Rewritten, Captured).
        var id = Guid.NewGuid();
        var passThrough = GateOutcome<string>.PassThrough("hi");
        var rewrote = GateOutcome<string>.Rewrote("hi2");
        var dropped = GateOutcome<string>.DropMessage();
        var captured = GateOutcome<string>.Capture(id);

        Assert.False(passThrough.Captured); Assert.False(passThrough.Dropped); Assert.False(passThrough.Rewritten);
        Assert.False(rewrote.Captured); Assert.False(rewrote.Dropped); Assert.True(rewrote.Rewritten);
        Assert.True(dropped.Dropped); Assert.False(dropped.Captured); Assert.False(dropped.Rewritten);
        Assert.True(captured.Captured); Assert.False(captured.Dropped); Assert.False(captured.Rewritten);
        Assert.Equal(id, captured.CapturedMessageId);
        Assert.Null(captured.Message);
    }
}

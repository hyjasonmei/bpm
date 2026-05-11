using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Sandbox;

/// <summary>
/// Schema round-trip checks for the new <see cref="SandboxCapturedMessage"/>
/// entity added in PR-J1. Confirms each channel persists its full payload
/// and that the indexes the Mailbox UI relies on (tenant + capturedAt,
/// processInstanceId) actually return rows in the expected order.
/// </summary>
public sealed class SandboxCapturedMessageRoundTripTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    public SandboxCapturedMessageRoundTripTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Email_round_trips_with_subject_and_bodies()
    {
        var id = Guid.NewGuid();
        var capturedAt = new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc);

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.Add(new SandboxCapturedMessage
            {
                Id = id,
                TenantCode = "default",
                Channel = SandboxChannel.Email,
                IntendedRecipientsJson = "[\"wilson@example.com\",\"manager@example.com\"]",
                Subject = "[BPM] 請假申請已送出",
                BodyHtml = "<p>Hello Wilson</p>",
                BodyText = "Hello Wilson",
                CapturedAt = capturedAt,
                OriginatingNotificationId = "manager_review_requested",
            });
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var loaded = await read.SandboxCapturedMessages.SingleAsync(x => x.Id == id);

        Assert.Equal(SandboxChannel.Email, loaded.Channel);
        Assert.Equal("[\"wilson@example.com\",\"manager@example.com\"]", loaded.IntendedRecipientsJson);
        Assert.Equal("[BPM] 請假申請已送出", loaded.Subject);
        Assert.Equal("<p>Hello Wilson</p>", loaded.BodyHtml);
        Assert.Equal("Hello Wilson", loaded.BodyText);
        Assert.Equal(capturedAt, loaded.CapturedAt);
        Assert.Equal("manager_review_requested", loaded.OriginatingNotificationId);
        Assert.Equal("[]", loaded.ReadByUserIdsJson);
    }

    [Fact]
    public async Task Webhook_round_trips_with_url_headers_and_payload()
    {
        var id = Guid.NewGuid();

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.Add(new SandboxCapturedMessage
            {
                Id = id,
                TenantCode = "default",
                Channel = SandboxChannel.Webhook,
                IntendedRecipientsJson = "[\"*\"]",
                Url = "https://customer.example.com/hooks/bpm",
                HeadersJson = "{\"X-BPM-Event\":\"leave.approved\",\"Content-Type\":\"application/json\"}",
                PayloadJson = "{\"instanceId\":\"abc\",\"status\":\"approved\"}",
                EventType = "leave.approved",
                CapturedAt = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc),
                OriginatingWebhookSubscriptionId = "sub-42",
            });
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var loaded = await read.SandboxCapturedMessages.SingleAsync(x => x.Id == id);

        Assert.Equal(SandboxChannel.Webhook, loaded.Channel);
        Assert.Equal("https://customer.example.com/hooks/bpm", loaded.Url);
        Assert.Contains("\"X-BPM-Event\":\"leave.approved\"", loaded.HeadersJson);
        Assert.Equal("{\"instanceId\":\"abc\",\"status\":\"approved\"}", loaded.PayloadJson);
        Assert.Equal("leave.approved", loaded.EventType);
        Assert.Equal("sub-42", loaded.OriginatingWebhookSubscriptionId);
    }

    [Fact]
    public async Task Sms_round_trips_with_body()
    {
        var id = Guid.NewGuid();

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.Add(new SandboxCapturedMessage
            {
                Id = id,
                TenantCode = "default",
                Channel = SandboxChannel.Sms,
                IntendedRecipientsJson = "[\"+886912345678\"]",
                Body = "您的請假已核准",
                CapturedAt = new DateTime(2026, 5, 11, 11, 0, 0, DateTimeKind.Utc),
            });
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var loaded = await read.SandboxCapturedMessages.SingleAsync(x => x.Id == id);

        Assert.Equal(SandboxChannel.Sms, loaded.Channel);
        Assert.Equal("您的請假已核准", loaded.Body);
        Assert.Equal("[\"+886912345678\"]", loaded.IntendedRecipientsJson);
        Assert.Null(loaded.Subject);
        Assert.Null(loaded.Url);
    }

    [Fact]
    public async Task Mailbox_query_orders_by_captured_at_descending_within_tenant()
    {
        var t0 = new DateTime(2026, 5, 11, 8, 0, 0, DateTimeKind.Utc);

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.AddRange(
                new SandboxCapturedMessage { TenantCode = "default", Channel = SandboxChannel.Email, Subject = "first", CapturedAt = t0 },
                new SandboxCapturedMessage { TenantCode = "default", Channel = SandboxChannel.Webhook, EventType = "leave.submitted", CapturedAt = t0.AddMinutes(5) },
                new SandboxCapturedMessage { TenantCode = "default", Channel = SandboxChannel.Sms, Body = "third", CapturedAt = t0.AddMinutes(10) },
                // Different tenant — must be excluded.
                new SandboxCapturedMessage { TenantCode = "other", Channel = SandboxChannel.Email, Subject = "other-tenant", CapturedAt = t0.AddMinutes(20) }
            );
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var rows = await read.SandboxCapturedMessages
            .Where(x => x.TenantCode == "default")
            .OrderByDescending(x => x.CapturedAt)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(SandboxChannel.Sms, rows[0].Channel);
        Assert.Equal(SandboxChannel.Webhook, rows[1].Channel);
        Assert.Equal(SandboxChannel.Email, rows[2].Channel);
    }

    [Fact]
    public async Task Per_instance_query_filters_to_one_instance()
    {
        var instanceA = Guid.NewGuid();
        var instanceB = Guid.NewGuid();
        var t0 = new DateTime(2026, 5, 11, 8, 0, 0, DateTimeKind.Utc);

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.AddRange(
                new SandboxCapturedMessage { ProcessInstanceId = instanceA, Channel = SandboxChannel.Email, Subject = "A1", CapturedAt = t0 },
                new SandboxCapturedMessage { ProcessInstanceId = instanceA, Channel = SandboxChannel.Email, Subject = "A2", CapturedAt = t0.AddMinutes(2) },
                new SandboxCapturedMessage { ProcessInstanceId = instanceB, Channel = SandboxChannel.Email, Subject = "B1", CapturedAt = t0.AddMinutes(1) },
                new SandboxCapturedMessage { ProcessInstanceId = null, Channel = SandboxChannel.Email, Subject = "system", CapturedAt = t0.AddMinutes(3) }
            );
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var rows = await read.SandboxCapturedMessages
            .Where(x => x.ProcessInstanceId == instanceA)
            .OrderByDescending(x => x.CapturedAt)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("A2", rows[0].Subject);
        Assert.Equal("A1", rows[1].Subject);
    }

    [Fact]
    public async Task Channel_stored_as_int_matches_enum_value()
    {
        var id = Guid.NewGuid();

        await using (var write = new AppDbContext(_options))
        {
            write.SandboxCapturedMessages.Add(new SandboxCapturedMessage
            {
                Id = id,
                TenantCode = "default",
                Channel = SandboxChannel.Webhook,
                CapturedAt = new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc),
            });
            await write.SaveChangesAsync();
        }

        await using var read = new AppDbContext(_options);
        var raw = await read.Database
            .SqlQueryRaw<int>("SELECT CAST(\"Channel\" AS INTEGER) AS Value FROM \"SandboxCapturedMessages\" WHERE \"Id\" = {0}", id)
            .SingleAsync();

        Assert.Equal((int)SandboxChannel.Webhook, raw);
    }
}

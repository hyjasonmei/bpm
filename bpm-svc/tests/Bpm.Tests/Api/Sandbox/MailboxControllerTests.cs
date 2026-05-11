using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Auth;
using Bpm.Api.Sandbox;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Sandbox;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Sandbox;

/// <summary>
/// PR-J4 §7: Mailbox controller endpoints (list / get / mark-read /
/// unread-count). Verified through SandboxController against an in-memory
/// SQLite DB so the EF.Functions.Like JSON-substring filter actually
/// executes.
/// </summary>
public sealed class MailboxControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _stub = new();
    private static readonly Guid CurrentUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public MailboxControllerTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_stub, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewCtx() => new(_options);

    private async Task SetSandboxAsync(bool on)
    {
        await using var db = NewCtx();
        var existing = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == "default");
        if (existing is null)
        {
            existing = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(existing);
        }
        existing.SandboxMode = on;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> InsertCapturedAsync(
        SandboxChannel channel, string? subject = null, string? eventType = null,
        Guid? processInstanceId = null, IEnumerable<string>? recipients = null,
        DateTime? capturedAt = null)
    {
        await using var db = NewCtx();
        var row = new SandboxCapturedMessage
        {
            TenantCode = "default",
            Channel = channel,
            Subject = subject,
            EventType = eventType,
            ProcessInstanceId = processInstanceId,
            IntendedRecipientsJson = JsonSerializer.Serialize(recipients ?? Array.Empty<string>()),
            CapturedAt = capturedAt ?? _stub.UtcNow,
        };
        db.SandboxCapturedMessages.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private SandboxController BuildController(out AppDbContext db, Guid userId)
    {
        db = NewCtx();
        var sys = new SystemClock();
        var sandboxClock = new SandboxClock(db, sys);
        var clockService = new SandboxClockService(
            db, sys, sandboxClock, new NoOpScheduledJobKicker(),
            NullLogger<SandboxClockService>.Instance);
        var resetService = new ResetService(db, clockService, NullLogger<ResetService>.Instance);
        var mailbox = new MailboxService(db, clockService);
        var jwt = new JwtTokenService(
            new JwtOptions { Secret = "test-secret-".PadRight(64, 'x') },
            new TestEnv());
        var controller = new SandboxController(
            new ThrowingSandboxService(), clockService, resetService, mailbox, db, jwt);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(userId) };
        return controller;
    }

    [Fact]
    public async Task ListCaptured_when_sandbox_off_returns_403()
    {
        await SetSandboxAsync(on: false);
        // Even though there are no rows, the endpoint should refuse before query.
        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured(null, null, null, false, 50, default);
            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_when_empty_returns_empty_list()
    {
        await SetSandboxAsync(on: true);
        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured(null, null, null, false, 50, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            Assert.Empty(rows);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_returns_all_three_channels_when_no_filter()
    {
        await SetSandboxAsync(on: true);
        await InsertCapturedAsync(SandboxChannel.Email, subject: "leave req");
        await InsertCapturedAsync(SandboxChannel.Webhook, eventType: "leave.approved");
        await InsertCapturedAsync(SandboxChannel.Sms);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured(null, null, null, false, 50, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            Assert.Equal(3, rows.Count);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_filters_by_channel()
    {
        await SetSandboxAsync(on: true);
        await InsertCapturedAsync(SandboxChannel.Email, subject: "e1");
        await InsertCapturedAsync(SandboxChannel.Webhook);
        await InsertCapturedAsync(SandboxChannel.Sms);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured("email", null, null, false, 50, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            var single = Assert.Single(rows);
            Assert.Equal(SandboxChannel.Email, single.Channel);
            Assert.Equal("e1", single.Subject);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_filters_by_processInstanceId()
    {
        await SetSandboxAsync(on: true);
        var instId = Guid.NewGuid();
        await InsertCapturedAsync(SandboxChannel.Email, subject: "match", processInstanceId: instId);
        await InsertCapturedAsync(SandboxChannel.Email, subject: "skip");

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured(null, null, instId, false, 50, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            var single = Assert.Single(rows);
            Assert.Equal("match", single.Subject);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task GetCaptured_returns_full_payload_when_found()
    {
        await SetSandboxAsync(on: true);
        var id = await InsertCapturedAsync(
            SandboxChannel.Email,
            subject: "请假",
            recipients: new[] { "wilson@example.com", "manager@example.com" });

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.GetCaptured(id, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var dto = Assert.IsType<CapturedMessageDetailDto>(ok.Value);
            Assert.Equal(id, dto.Id);
            Assert.Equal("请假", dto.Subject);
            Assert.Equal(2, dto.IntendedRecipients.Count);
            Assert.False(dto.ReadByMe);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task GetCaptured_returns_404_when_missing()
    {
        await SetSandboxAsync(on: true);
        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.GetCaptured(Guid.NewGuid(), default);
            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task MarkCapturedRead_sets_ReadByMe_true_on_subsequent_list()
    {
        await SetSandboxAsync(on: true);
        var id = await InsertCapturedAsync(SandboxChannel.Email, subject: "x");

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var markResult = await ctrl.MarkCapturedRead(id, default);
            Assert.IsType<OkObjectResult>(markResult);

            var listResult = await ctrl.ListCaptured(null, null, null, false, 50, default);
            var ok = Assert.IsType<OkObjectResult>(listResult);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            var single = Assert.Single(rows);
            Assert.True(single.ReadByMe);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task MarkCapturedRead_is_idempotent()
    {
        await SetSandboxAsync(on: true);
        var id = await InsertCapturedAsync(SandboxChannel.Email);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            await ctrl.MarkCapturedRead(id, default);
            await ctrl.MarkCapturedRead(id, default);
        }
        finally { db.Dispose(); }

        // Verify the JSON only contains one entry.
        await using var verify = NewCtx();
        var row = await verify.SandboxCapturedMessages.SingleAsync(x => x.Id == id);
        var readers = JsonSerializer.Deserialize<List<string>>(row.ReadByUserIdsJson)!;
        Assert.Single(readers);
        Assert.Equal(CurrentUserId.ToString(), readers[0]);
    }

    [Fact]
    public async Task UnreadCount_when_sandbox_off_returns_zero_without_db_hit()
    {
        await SetSandboxAsync(on: false);
        // Insert rows directly so we can verify they're NOT counted (sandbox-off
        // path is supposed to short-circuit before querying).
        // Even though we insert with sandbox off, the row still lands — the
        // gate is upstream. The test is really: counts are 0 when sandbox off
        // even with rows present.
        await InsertCapturedAsync(SandboxChannel.Email);
        await InsertCapturedAsync(SandboxChannel.Webhook);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var dto = await ctrl.UnreadCount(default);
            Assert.Equal(0, dto.Total);
            Assert.Equal(0, dto.ByChannel["Email"]);
            Assert.Equal(0, dto.ByChannel["Webhook"]);
            Assert.Equal(0, dto.ByChannel["Sms"]);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task UnreadCount_when_sandbox_on_returns_correct_per_channel_breakdown()
    {
        await SetSandboxAsync(on: true);
        await InsertCapturedAsync(SandboxChannel.Email);
        await InsertCapturedAsync(SandboxChannel.Email);
        await InsertCapturedAsync(SandboxChannel.Webhook);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var dto = await ctrl.UnreadCount(default);
            Assert.Equal(3, dto.Total);
            Assert.Equal(2, dto.ByChannel["Email"]);
            Assert.Equal(1, dto.ByChannel["Webhook"]);
            Assert.Equal(0, dto.ByChannel["Sms"]);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task UnreadCount_excludes_messages_already_read_by_current_user()
    {
        await SetSandboxAsync(on: true);
        var read = await InsertCapturedAsync(SandboxChannel.Email);
        await InsertCapturedAsync(SandboxChannel.Email);

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            await ctrl.MarkCapturedRead(read, default);

            var dto = await ctrl.UnreadCount(default);
            Assert.Equal(1, dto.Total);
            Assert.Equal(1, dto.ByChannel["Email"]);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_with_unread_filter_excludes_read_rows()
    {
        await SetSandboxAsync(on: true);
        var readId = await InsertCapturedAsync(SandboxChannel.Email, subject: "read");
        await InsertCapturedAsync(SandboxChannel.Email, subject: "unread");

        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            await ctrl.MarkCapturedRead(readId, default);

            var result = await ctrl.ListCaptured(null, null, null, unread: true, 50, default);
            var ok = Assert.IsType<OkObjectResult>(result);
            var rows = Assert.IsAssignableFrom<IReadOnlyList<CapturedMessageSummaryDto>>(ok.Value);
            var single = Assert.Single(rows);
            Assert.Equal("unread", single.Subject);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ListCaptured_with_invalid_channel_returns_400()
    {
        await SetSandboxAsync(on: true);
        var ctrl = BuildController(out var db, CurrentUserId);
        try
        {
            var result = await ctrl.ListCaptured("not-a-channel", null, null, false, 50, default);
            Assert.IsType<BadRequestObjectResult>(result);
        }
        finally { db.Dispose(); }
    }

    private static DefaultHttpContext HttpContextFor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("roles", "admin"),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class ThrowingSandboxService : ISandboxService
    {
        public Task<SandboxStatusDto> GetStatusAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<SandboxStatusDto> SetStatusAsync(UpdateSandboxRequest req, Guid actorUserId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

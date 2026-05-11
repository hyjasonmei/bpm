using System.Security.Claims;
using Bpm.Api.Sandbox;
using Bpm.Application.Common.Abstractions;
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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Sandbox;

/// <summary>
/// PR-J3 §4 + §8 — Sandbox clock controller endpoints. Constructs
/// SandboxController + a real SandboxClockService against an in-memory
/// SQLite DB so we exercise the persistence path end-to-end.
/// </summary>
public sealed class SandboxClockControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _stub = new(new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc));
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public SandboxClockControllerTests()
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

    private async Task SetSandboxAsync(bool on, long offsetSec = 0)
    {
        await using var db = NewCtx();
        var existing = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == "default");
        if (existing is null)
        {
            existing = new TenantSettings { TenantCode = "default" };
            db.TenantSettings.Add(existing);
        }
        existing.SandboxMode = on;
        existing.SandboxClockOffsetSeconds = offsetSec;
        await db.SaveChangesAsync();
    }

    private SandboxController BuildController(out AppDbContext db)
    {
        db = NewCtx();
        var systemClock = new FixedSystemClock(_stub.UtcNow);
        var effective = new SandboxClock(db, systemClock);
        var clockService = new SandboxClockService(
            db,
            systemClock,
            effective,
            new NoOpScheduledJobKicker(),
            NullLogger<SandboxClockService>.Instance);
        var controller = new SandboxController(new ThrowingSandboxService(), clockService);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(AdminId) };
        return controller;
    }

    [Fact]
    public async Task Get_clock_when_sandbox_off_returns_real_now_with_zero_offset()
    {
        await SetSandboxAsync(on: false);

        var controller = BuildController(out var db);
        try
        {
            var dto = await controller.GetClock(default);

            Assert.Equal(_stub.UtcNow, dto.RealNow);
            Assert.Equal(_stub.UtcNow, dto.SandboxNow);
            Assert.Equal(0, dto.OffsetSeconds);
            Assert.False(dto.SandboxOn);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task Advance_when_sandbox_off_returns_400_sandbox_off()
    {
        await SetSandboxAsync(on: false);

        var controller = BuildController(out var db);
        try
        {
            var result = await controller.AdvanceClock(new AdvanceClockRequest(Days: 1), default);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var body = Assert.IsAssignableFrom<object>(bad.Value!);
            Assert.Contains("sandbox_off", body.ToString()!);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task Reset_when_sandbox_off_returns_400_sandbox_off()
    {
        await SetSandboxAsync(on: false);

        var controller = BuildController(out var db);
        try
        {
            var result = await controller.ResetClock(default);

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("sandbox_off", bad.Value!.ToString()!);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task Advance_when_sandbox_on_updates_offset_and_subsequent_get_reflects()
    {
        await SetSandboxAsync(on: true);

        var controller = BuildController(out var db);
        try
        {
            var advance = await controller.AdvanceClock(new AdvanceClockRequest(Hours: 2), default);
            var ok = Assert.IsType<OkObjectResult>(advance);
            var dto = Assert.IsType<SandboxClockDto>(ok.Value);
            Assert.Equal(7200, dto.OffsetSeconds);
            Assert.Equal(_stub.UtcNow.AddHours(2), dto.SandboxNow);
            Assert.True(dto.SandboxOn);
        }
        finally { db.Dispose(); }

        // Verify persisted state via a fresh controller (new scope).
        var verify = BuildController(out var db2);
        try
        {
            var get = await verify.GetClock(default);
            Assert.Equal(7200, get.OffsetSeconds);
            Assert.Equal(_stub.UtcNow.AddHours(2), get.SandboxNow);
        }
        finally { db2.Dispose(); }
    }

    [Fact]
    public async Task Advance_accumulates_across_multiple_calls()
    {
        await SetSandboxAsync(on: true);

        // First +1h.
        var ctrl1 = BuildController(out var db1);
        try
        {
            await ctrl1.AdvanceClock(new AdvanceClockRequest(Hours: 1), default);
        }
        finally { db1.Dispose(); }

        // Then +30m. Total should be 5400s.
        var ctrl2 = BuildController(out var db2);
        try
        {
            var second = await ctrl2.AdvanceClock(new AdvanceClockRequest(Minutes: 30), default);
            var dto = Assert.IsType<SandboxClockDto>(((OkObjectResult)second).Value);
            Assert.Equal(5400, dto.OffsetSeconds);
        }
        finally { db2.Dispose(); }
    }

    [Fact]
    public async Task Advance_with_days_hours_minutes_seconds_sums_correctly()
    {
        await SetSandboxAsync(on: true);

        var controller = BuildController(out var db);
        try
        {
            var result = await controller.AdvanceClock(
                new AdvanceClockRequest(Days: 1, Hours: 2, Minutes: 3, Seconds: 4),
                default);
            var dto = Assert.IsType<SandboxClockDto>(((OkObjectResult)result).Value);
            // 86400 + 7200 + 180 + 4
            Assert.Equal(93_784, dto.OffsetSeconds);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task Reset_clears_offset_back_to_zero()
    {
        await SetSandboxAsync(on: true, offsetSec: 86_400);

        var controller = BuildController(out var db);
        try
        {
            var result = await controller.ResetClock(default);
            var dto = Assert.IsType<SandboxClockDto>(((OkObjectResult)result).Value);
            Assert.Equal(0, dto.OffsetSeconds);
            Assert.Equal(_stub.UtcNow, dto.SandboxNow);
        }
        finally { db.Dispose(); }

        // Persisted state.
        await using var verify = NewCtx();
        var row = await verify.TenantSettings.SingleAsync();
        Assert.Equal(0, row.SandboxClockOffsetSeconds);
    }

    [Fact]
    public async Task Get_when_sandbox_on_with_existing_offset_returns_shifted_time()
    {
        await SetSandboxAsync(on: true, offsetSec: 3600);

        var controller = BuildController(out var db);
        try
        {
            var dto = await controller.GetClock(default);
            Assert.Equal(_stub.UtcNow, dto.RealNow);
            Assert.Equal(_stub.UtcNow.AddHours(1), dto.SandboxNow);
            Assert.Equal(3600, dto.OffsetSeconds);
            Assert.True(dto.SandboxOn);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task Advance_negative_offset_is_allowed_offset_can_go_below_zero()
    {
        // The service intentionally permits negative deltas — the v1 PR-J3 spec
        // doesn't forbid them and "advance by -1h" is a useful manual nudge.
        // (The original openspec tasks.md mentioned forbidding backward time
        // but the PR-J3 prompt narrowed scope: arithmetic only, no sign rule.)
        await SetSandboxAsync(on: true, offsetSec: 7200);

        var controller = BuildController(out var db);
        try
        {
            var result = await controller.AdvanceClock(new AdvanceClockRequest(Hours: -1), default);
            var dto = Assert.IsType<SandboxClockDto>(((OkObjectResult)result).Value);
            Assert.Equal(3600, dto.OffsetSeconds);
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

    /// <summary>SystemClock subclass returning a fixed UtcNow for assertion stability.</summary>
    private sealed class FixedSystemClock(DateTime fixedNow) : SystemClock
    {
        public override DateTime UtcNow { get; } = fixedNow;
    }

    /// <summary>SandboxController demands ISandboxService too — these tests don't touch it.</summary>
    private sealed class ThrowingSandboxService : ISandboxService
    {
        public Task<SandboxStatusDto> GetStatusAsync(CancellationToken ct = default)
            => throw new NotSupportedException("clock-only test fixture");
        public Task<SandboxStatusDto> SetStatusAsync(
            UpdateSandboxRequest req, Guid actorUserId, CancellationToken ct = default)
            => throw new NotSupportedException("clock-only test fixture");
        public Task<IReadOnlyList<SandboxRedirectDto>> GetRedirectsAsync(int days, CancellationToken ct = default)
            => throw new NotSupportedException("clock-only test fixture");
    }
}

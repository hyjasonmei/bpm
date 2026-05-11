using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Common;

/// <summary>
/// PR-J3 §4.9 — SandboxClock decorator behavioural coverage.
///
/// SandboxClock wraps SystemClock and adds <c>TenantSettings.SandboxClockOffsetSeconds</c>
/// when sandbox mode is on. The snapshot is cached for the life of the
/// instance so a single request only reads the row once.
/// </summary>
public sealed class SandboxClockTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _innerStub = new(new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc));

    public SandboxClockTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_innerStub, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var seed = new AppDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    private AppDbContext NewCtx() => new(_options);

    private async Task SetSandboxAsync(bool on, long offsetSec)
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

    /// <summary>SystemClock-shaped stub so the decorator gets a deterministic "real now".</summary>
    private SystemClockProxy StubSystemClock() => new(_innerStub.UtcNow);

    [Fact]
    public void Sandbox_off_returns_pass_through_real_time()
    {
        // No TenantSettings row at all → defaults (off, 0).
        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());

        Assert.Equal(_innerStub.UtcNow, clock.UtcNow);
    }

    [Fact]
    public async Task Sandbox_on_with_zero_offset_still_returns_real_time()
    {
        await SetSandboxAsync(on: true, offsetSec: 0);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());

        Assert.Equal(_innerStub.UtcNow, clock.UtcNow);
    }

    [Fact]
    public async Task Sandbox_on_with_one_day_offset_shifts_utcnow_forward()
    {
        await SetSandboxAsync(on: true, offsetSec: 86_400);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());

        Assert.Equal(_innerStub.UtcNow.AddDays(1), clock.UtcNow);
    }

    [Fact]
    public async Task Sandbox_off_ignores_persisted_offset()
    {
        // Defensive: even if a stale offset row exists, sandbox=off masks it.
        await SetSandboxAsync(on: false, offsetSec: 86_400);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());

        Assert.Equal(_innerStub.UtcNow, clock.UtcNow);
    }

    [Fact]
    public async Task TodayInTaipei_reflects_sandbox_offset()
    {
        // Real now = 2026-05-11 12:00 UTC = 2026-05-11 20:00 Taipei.
        // Advance +12h → sandbox now = 2026-05-12 00:00 UTC = 2026-05-12 08:00 Taipei.
        await SetSandboxAsync(on: true, offsetSec: 12 * 3600);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());

        Assert.Equal(new DateOnly(2026, 5, 12), clock.TodayInTaipei());
    }

    [Fact]
    public async Task Snapshot_is_cached_per_instance_so_second_read_does_not_hit_db()
    {
        await SetSandboxAsync(on: true, offsetSec: 3600);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());
        var first = clock.UtcNow;

        // Mutate the underlying row through a separate context. If SandboxClock
        // were re-reading every call, the next read would jump by 7200s instead
        // of staying at +3600s.
        await using (var mutator = NewCtx())
        {
            var row = await mutator.TenantSettings.FirstAsync();
            row.SandboxClockOffsetSeconds = 7200;
            await mutator.SaveChangesAsync();
        }

        var second = clock.UtcNow;
        Assert.Equal(first, second);
        Assert.Equal(_innerStub.UtcNow.AddHours(1), second); // Still cached at +1h.
    }

    [Fact]
    public async Task InvalidateSnapshot_lets_caller_observe_offset_change_within_same_scope()
    {
        await SetSandboxAsync(on: true, offsetSec: 3600);

        using var db = NewCtx();
        var clock = new SandboxClock(db, StubSystemClock());
        var first = clock.UtcNow;
        Assert.Equal(_innerStub.UtcNow.AddHours(1), first);

        // SandboxClockService writes a new offset, then asks the decorator to
        // refresh so the same request sees the new time.
        await using (var mutator = NewCtx())
        {
            var row = await mutator.TenantSettings.FirstAsync();
            row.SandboxClockOffsetSeconds = 86_400;
            await mutator.SaveChangesAsync();
        }
        clock.InvalidateSnapshot();

        Assert.Equal(_innerStub.UtcNow.AddDays(1), clock.UtcNow);
    }

    /// <summary>
    /// Local SystemClock-shaped stub so the decorator can be constructed with
    /// a deterministic "real now" without needing the production
    /// <see cref="SystemClock"/> (which reads <c>DateTime.UtcNow</c>).
    /// </summary>
    private sealed class SystemClockProxy : SystemClock
    {
        private readonly DateTime _fixed;
        public SystemClockProxy(DateTime fixedNow) { _fixed = fixedNow; }
        public override DateTime UtcNow => _fixed;
    }
}

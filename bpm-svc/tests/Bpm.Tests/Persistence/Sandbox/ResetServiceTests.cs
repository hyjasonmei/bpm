using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.Common;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Sandbox;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Persistence.Sandbox;

/// <summary>
/// ResetService behavioural coverage. Since the Model-A process runtime was
/// removed, reset only clears Model-B case tables + captured sandbox messages
/// (+ the clock offset on ResetAll). These tests pin the sandbox-on gate, the
/// per-instance captured-message scoping, and the tenant-settings preservation.
/// </summary>
public sealed class ResetServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _stub = new();
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public ResetServiceTests()
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

    private ResetService BuildService(out AppDbContext db)
    {
        db = NewCtx();
        var sys = new SystemClock();
        var sandboxClock = new SandboxClock(db, sys);
        var clockService = new SandboxClockService(
            db, sys, sandboxClock, new NoOpScheduledJobKicker(),
            NullLogger<SandboxClockService>.Instance);
        return new ResetService(db, clockService, NullLogger<ResetService>.Instance);
    }

    private static async Task AddCapturedAsync(AppDbContext db, Guid? processInstanceId, string tenantCode = "default")
    {
        db.SandboxCapturedMessages.Add(new SandboxCapturedMessage
        {
            TenantCode = tenantCode,
            ProcessInstanceId = processInstanceId,
            Channel = SandboxChannel.Email,
            Subject = "test",
            CapturedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResetInstanceAsync_when_sandbox_off_throws_SandboxOffException()
    {
        await SetSandboxAsync(on: false);

        var svc = BuildService(out var db);
        try
        {
            await Assert.ThrowsAsync<SandboxOffException>(
                () => svc.ResetInstanceAsync(Guid.NewGuid(), AdminId));
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ResetInstanceAsync_deletes_captured_for_that_instance_only()
    {
        await SetSandboxAsync(on: true);

        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await using (var seed = NewCtx())
        {
            await AddCapturedAsync(seed, targetId);
            await AddCapturedAsync(seed, otherId);
        }

        var svc = BuildService(out var db);
        ResetSummary summary;
        try
        {
            summary = await svc.ResetInstanceAsync(targetId, AdminId);
        }
        finally { db.Dispose(); }

        Assert.Equal(1, summary.CapturedMessagesDeleted);

        await using var verify = NewCtx();
        Assert.False(await verify.SandboxCapturedMessages.AnyAsync(x => x.ProcessInstanceId == targetId));
        Assert.True(await verify.SandboxCapturedMessages.AnyAsync(x => x.ProcessInstanceId == otherId));
    }

    [Fact]
    public async Task ResetInstanceAsync_returns_zero_summary_when_nothing_matches()
    {
        await SetSandboxAsync(on: true);

        var svc = BuildService(out var db);
        try
        {
            var summary = await svc.ResetInstanceAsync(Guid.NewGuid(), AdminId);
            Assert.Equal(0, summary.CapturedMessagesDeleted);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ResetAllAsync_when_sandbox_off_throws_SandboxOffException()
    {
        await SetSandboxAsync(on: false);

        var svc = BuildService(out var db);
        try
        {
            await Assert.ThrowsAsync<SandboxOffException>(() => svc.ResetAllAsync(AdminId));
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ResetAllAsync_wipes_captured_and_resets_clock_offset()
    {
        await SetSandboxAsync(on: true);

        // Set a non-zero clock offset that ResetAllAsync should clear.
        await using (var prep = NewCtx())
        {
            var t = await prep.TenantSettings.FirstAsync();
            t.SandboxClockOffsetSeconds = 86_400;
            await prep.SaveChangesAsync();
        }

        await using (var seed = NewCtx())
        {
            await AddCapturedAsync(seed, Guid.NewGuid());
            await AddCapturedAsync(seed, Guid.NewGuid());
        }

        var svc = BuildService(out var db);
        ResetSummary summary;
        try
        {
            summary = await svc.ResetAllAsync(AdminId);
        }
        finally { db.Dispose(); }

        Assert.Equal(2, summary.CapturedMessagesDeleted);

        await using var verify = NewCtx();
        Assert.Empty(await verify.SandboxCapturedMessages.ToListAsync());

        var tenant = await verify.TenantSettings.SingleAsync();
        Assert.Equal(0, tenant.SandboxClockOffsetSeconds);
        // Sandbox mode itself stays on — tester wants to keep testing.
        Assert.True(tenant.SandboxMode);
    }

    [Fact]
    public async Task ResetAllAsync_does_not_delete_tenant_settings_row()
    {
        await SetSandboxAsync(on: true);

        await using (var seed = NewCtx())
        {
            await AddCapturedAsync(seed, Guid.NewGuid());
        }

        var svc = BuildService(out var db);
        try
        {
            await svc.ResetAllAsync(AdminId);
        }
        finally { db.Dispose(); }

        await using var verify = NewCtx();
        // TenantSettings row itself preserved (just offset cleared, sandbox flag intact).
        Assert.NotNull(await verify.TenantSettings.FirstOrDefaultAsync());
    }
}

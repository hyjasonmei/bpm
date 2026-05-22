using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Process;
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
/// PR-J4 §5: ResetService behavioural coverage. The key invariant under
/// test is that <c>ExecuteDeleteAsync</c> bypasses the
/// <see cref="AuditSaveChangesInterceptor"/>'s TaskHistory append-only
/// guard — so the service can do its job without needing an
/// <c>IResetContext</c> "I'm allowed to delete" flag.
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

    private static async Task<Guid> CreateInstanceWithChildrenAsync(
        AppDbContext db, string tenantCode = "default")
    {
        var inst = new ProcessInstance
        {
            TenantCode = tenantCode,
            SpecCode = "LEAVE_REQUEST",
            SpecVersion = 1,
            InitiatorUserId = Guid.NewGuid(),
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
        };
        db.ProcessInstances.Add(inst);

        db.ProcessTasks.Add(new ProcessTask
        {
            TenantCode = tenantCode,
            ProcessInstanceId = inst.Id,
            NodeId = "task_apply",
            NodeKind = NodeKind.UserTask,
        });
        db.ProcessTasks.Add(new ProcessTask
        {
            TenantCode = tenantCode,
            ProcessInstanceId = inst.Id,
            NodeId = "task_approve",
            NodeKind = NodeKind.Approval,
        });

        db.TaskHistory.Add(new TaskHistory
        {
            TenantCode = tenantCode,
            ProcessInstanceId = inst.Id,
            EventType = HistoryEventType.InstanceStarted,
        });
        db.TaskHistory.Add(new TaskHistory
        {
            TenantCode = tenantCode,
            ProcessInstanceId = inst.Id,
            EventType = HistoryEventType.TaskSpawned,
        });

        db.SandboxCapturedMessages.Add(new SandboxCapturedMessage
        {
            TenantCode = tenantCode,
            ProcessInstanceId = inst.Id,
            Channel = SandboxChannel.Email,
            Subject = "test",
            CapturedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return inst.Id;
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
    public async Task ResetInstanceAsync_deletes_all_related_rows_and_returns_counts()
    {
        await SetSandboxAsync(on: true);

        Guid instanceId;
        await using (var seed = NewCtx())
        {
            instanceId = await CreateInstanceWithChildrenAsync(seed);
        }

        var svc = BuildService(out var db);
        ResetSummary summary;
        try
        {
            summary = await svc.ResetInstanceAsync(instanceId, AdminId);
        }
        finally { db.Dispose(); }

        Assert.Equal(1, summary.InstancesDeleted);
        Assert.Equal(2, summary.TasksDeleted);
        Assert.Equal(2, summary.HistoryRowsDeleted);
        Assert.Equal(1, summary.CapturedMessagesDeleted);

        // Verify in a fresh context — TaskHistory MUST be gone even though
        // the audit interceptor treats it as append-only. ExecuteDeleteAsync
        // bypasses the interceptor so the delete is allowed.
        await using var verify = NewCtx();
        Assert.False(await verify.ProcessInstances.AnyAsync(x => x.Id == instanceId));
        Assert.False(await verify.ProcessTasks.AnyAsync(x => x.ProcessInstanceId == instanceId));
        Assert.False(await verify.TaskHistory.AnyAsync(x => x.ProcessInstanceId == instanceId));
        Assert.False(await verify.SandboxCapturedMessages.AnyAsync(x => x.ProcessInstanceId == instanceId));
    }

    [Fact]
    public async Task ResetInstanceAsync_does_not_touch_other_instances()
    {
        await SetSandboxAsync(on: true);

        Guid targetId, otherId;
        await using (var seed = NewCtx())
        {
            targetId = await CreateInstanceWithChildrenAsync(seed);
            otherId = await CreateInstanceWithChildrenAsync(seed);
        }

        var svc = BuildService(out var db);
        try
        {
            await svc.ResetInstanceAsync(targetId, AdminId);
        }
        finally { db.Dispose(); }

        await using var verify = NewCtx();
        Assert.True(await verify.ProcessInstances.AnyAsync(x => x.Id == otherId));
        Assert.Equal(2, await verify.ProcessTasks.CountAsync(x => x.ProcessInstanceId == otherId));
        Assert.Equal(2, await verify.TaskHistory.CountAsync(x => x.ProcessInstanceId == otherId));
        Assert.Equal(1, await verify.SandboxCapturedMessages.CountAsync(x => x.ProcessInstanceId == otherId));
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
    public async Task ResetAllAsync_wipes_all_default_tenant_rows_and_resets_clock_offset()
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
            await CreateInstanceWithChildrenAsync(seed);
            await CreateInstanceWithChildrenAsync(seed);
        }

        var svc = BuildService(out var db);
        ResetSummary summary;
        try
        {
            summary = await svc.ResetAllAsync(AdminId);
        }
        finally { db.Dispose(); }

        Assert.Equal(2, summary.InstancesDeleted);
        Assert.Equal(4, summary.TasksDeleted);
        Assert.Equal(4, summary.HistoryRowsDeleted);
        Assert.Equal(2, summary.CapturedMessagesDeleted);

        await using var verify = NewCtx();
        Assert.Empty(await verify.ProcessInstances.ToListAsync());
        Assert.Empty(await verify.ProcessTasks.ToListAsync());
        Assert.Empty(await verify.TaskHistory.ToListAsync());
        Assert.Empty(await verify.SandboxCapturedMessages.ToListAsync());

        var tenant = await verify.TenantSettings.SingleAsync();
        Assert.Equal(0, tenant.SandboxClockOffsetSeconds);
        // Sandbox mode itself stays on — tester wants to keep testing.
        Assert.True(tenant.SandboxMode);
    }

    [Fact]
    public async Task ResetInstanceAsync_returns_zero_summary_when_instance_does_not_exist()
    {
        await SetSandboxAsync(on: true);

        var svc = BuildService(out var db);
        try
        {
            var summary = await svc.ResetInstanceAsync(Guid.NewGuid(), AdminId);
            Assert.Equal(0, summary.InstancesDeleted);
            Assert.Equal(0, summary.TasksDeleted);
            Assert.Equal(0, summary.HistoryRowsDeleted);
            Assert.Equal(0, summary.CapturedMessagesDeleted);
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public async Task ResetAllAsync_does_not_delete_specs_or_tenant_settings_row()
    {
        await SetSandboxAsync(on: true);

        await using (var seed = NewCtx())
        {
            await CreateInstanceWithChildrenAsync(seed);
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

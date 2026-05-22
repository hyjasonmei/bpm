using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// PR-J3: writes the new offset to <c>TenantSettings.SandboxClockOffsetSeconds</c>
/// and (after success) invalidates the per-scope <see cref="SandboxClock"/>
/// cache so the same request observes the shifted clock immediately.
///
/// Audit table for clock events is intentionally skipped for v1 (logged at
/// Info level instead) — see PR-J3 §4.8 in tasks.md.
/// </summary>
public sealed class SandboxClockService(
    AppDbContext db,
    SystemClock systemClock,
    IClock effectiveClock,
    IScheduledJobKicker kicker,
    ILogger<SandboxClockService> logger) : ISandboxClockService
{
    private const string DefaultTenant = "default";

    public async Task<SandboxClockDto> GetAsync(CancellationToken ct = default)
    {
        var s = await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantCode == DefaultTenant, ct);
        return Build(s);
    }

    public async Task<SandboxClockDto> AdvanceAsync(int days, int hours, int minutes, int seconds, CancellationToken ct = default)
    {
        var addSec = checked(((long)days * 86400L) + ((long)hours * 3600L) + ((long)minutes * 60L) + seconds);

        var s = await GetOrCreateAsync(ct);
        if (!s.SandboxMode) throw new SandboxOffException();

        s.SandboxClockOffsetSeconds = checked(s.SandboxClockOffsetSeconds + addSec);
        await db.SaveChangesAsync(ct);

        // Invalidate the SandboxClock cache so the same request sees the new offset.
        if (effectiveClock is SandboxClock sbc) sbc.InvalidateSnapshot();

        logger.LogInformation(
            "sandbox clock advanced by {AddSec}s (new offset {OffsetSec}s) — would trigger scheduled job pass",
            addSec, s.SandboxClockOffsetSeconds);

        // TODO: when SLA timers / webhook dispatch ship, the kicker will pulse
        // their queues so testers see consequences without waiting.
        await kicker.KickAsync(ct);

        return Build(s);
    }

    public async Task<SandboxClockDto> ResetAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateAsync(ct);
        if (!s.SandboxMode) throw new SandboxOffException();

        s.SandboxClockOffsetSeconds = 0;
        await db.SaveChangesAsync(ct);

        if (effectiveClock is SandboxClock sbc) sbc.InvalidateSnapshot();

        logger.LogInformation("sandbox clock reset to 0 offset");
        return Build(s);
    }

    private async Task<TenantSettings> GetOrCreateAsync(CancellationToken ct)
    {
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantCode == DefaultTenant, ct);
        if (s is not null) return s;
        s = new TenantSettings { TenantCode = DefaultTenant };
        db.TenantSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private SandboxClockDto Build(TenantSettings? s)
    {
        var realNow = systemClock.UtcNow;
        var offset = s?.SandboxClockOffsetSeconds ?? 0;
        var sandboxOn = s?.SandboxMode ?? false;
        var sandboxNow = sandboxOn ? realNow.AddSeconds(offset) : realNow;
        return new SandboxClockDto(realNow, sandboxNow, offset, sandboxOn);
    }
}

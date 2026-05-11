using Bpm.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Common;

/// <summary>
/// PR-J3: transparent decorator over <see cref="SystemClock"/> that adds
/// <c>TenantSettings.SandboxClockOffsetSeconds</c> when sandbox mode is on.
///
/// Scoped lifetime — depends on <see cref="AppDbContext"/> (Scoped). The
/// sandbox snapshot is cached for the life of the scope so a single HTTP
/// request that asks for <c>UtcNow</c> ten times still hits the DB once.
///
/// Existing IClock callers (gates, runtime, evaluators, audit interceptor)
/// transparently get sandbox-shifted time when the toggle is on, real time
/// when off. The interface signature is unchanged.
/// </summary>
public sealed class SandboxClock(AppDbContext db, SystemClock inner) : IClock
{
    private const string DefaultTenant = "default";

    public DateTime UtcNow
    {
        get
        {
            var realNow = inner.UtcNow;
            var (sandboxOn, offsetSec) = LoadSnapshotOnce();
            return sandboxOn ? realNow.AddSeconds(offsetSec) : realNow;
        }
    }

    public DateOnly TodayInTaipei()
    {
        // Reuse the same shifted UtcNow so the date and time stay coherent
        // — otherwise advancing the clock by 12h could leave TodayInTaipei
        // pointing at the real-world date.
        var nowUtc = UtcNow;
        var nowTaipei = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Taipei);
        return DateOnly.FromDateTime(nowTaipei);
    }

    private static readonly TimeZoneInfo Taipei = ResolveTaipei();
    private static TimeZoneInfo ResolveTaipei()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }

    // Cached for the scope. EF query runs sync; the row is tiny (single
    // tenant, single index lookup) and only fires on the first UtcNow read.
    private (bool SandboxOn, long OffsetSec)? _snapshot;

    private (bool, long) LoadSnapshotOnce()
    {
        if (_snapshot is null)
        {
            var s = db.TenantSettings
                .AsNoTracking()
                .FirstOrDefault(x => x.TenantCode == DefaultTenant);
            _snapshot = (s?.SandboxMode ?? false, s?.SandboxClockOffsetSeconds ?? 0);
        }
        return _snapshot.Value;
    }

    /// <summary>
    /// Used by <c>SandboxClockService</c> after writing a new offset so the
    /// next UtcNow read inside the same request reflects the change.
    /// Public (not internal) so tests in <c>Bpm.Tests</c> can exercise it
    /// without needing <c>InternalsVisibleTo</c> plumbing.
    /// </summary>
    public void InvalidateSnapshot() => _snapshot = null;
}

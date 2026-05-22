using NodaTime;
using AppIClock = Bpm.Admin.Application.Common.Abstractions.IClock;

namespace Bpm.Admin.Api.Common;

/// <summary>
/// IClock backed by the system + NodaTime for Asia/Taipei conversion.
/// Mirrors the bpm-svc SystemClock; admin-svc needs it for CEL
/// expression evaluation (now / today). Aliased to avoid clashing with
/// NodaTime's own SystemClock.
/// </summary>
public sealed class AdminSystemClock : AppIClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayInTaipei()
    {
        var tz = DateTimeZoneProviders.Tzdb["Asia/Taipei"];
        var nowInTaipei = NodaTime.SystemClock.Instance.GetCurrentInstant().InZone(tz);
        return DateOnly.FromDateTime(nowInTaipei.Date.ToDateTimeUnspecified());
    }
}

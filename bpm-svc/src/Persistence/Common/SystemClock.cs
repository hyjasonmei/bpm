using Bpm.Application.Common.Abstractions;

namespace Bpm.Persistence.Common;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Taipei = ResolveTaipei();

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly TodayInTaipei()
    {
        var nowTaipei = TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Taipei);
        return DateOnly.FromDateTime(nowTaipei);
    }

    private static TimeZoneInfo ResolveTaipei()
    {
        // .NET 10 ICU accepts "Asia/Taipei" cross-platform. Fallback to Windows id just in case.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time"); }
    }
}

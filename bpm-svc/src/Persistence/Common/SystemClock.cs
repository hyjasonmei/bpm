using Bpm.Application.Common.Abstractions;

namespace Bpm.Persistence.Common;

public class SystemClock : IClock
{
    private static readonly TimeZoneInfo Taipei = ResolveTaipei();

    // Virtual so unit tests for the SandboxClock decorator can subclass with
    // a fixed UtcNow (otherwise the decorator's "real now" varies per call,
    // making offset assertions flaky).
    public virtual DateTime UtcNow => DateTime.UtcNow;

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

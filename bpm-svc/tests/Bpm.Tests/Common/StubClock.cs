using Bpm.Application.Common.Abstractions;

namespace Bpm.Tests.Common;

internal sealed class StubClock(DateTime? utcNow = null) : IClock
{
    public DateTime UtcNow { get; } = utcNow ?? new DateTime(2026, 5, 11, 12, 0, 0, DateTimeKind.Utc);
    public DateOnly TodayInTaipei() => DateOnly.FromDateTime(UtcNow.AddHours(8));
}

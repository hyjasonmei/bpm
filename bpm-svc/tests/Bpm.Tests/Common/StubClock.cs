using Bpm.Application.Common.Abstractions;

namespace Bpm.Tests.Common;

internal sealed class StubClock(DateTime fixedNow) : IClock
{
    public DateTime UtcNow { get; set; } = fixedNow;
    public DateOnly TodayInTaipei() => DateOnly.FromDateTime(UtcNow);
}

namespace Bpm.Application.Common.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly TodayInTaipei();
}

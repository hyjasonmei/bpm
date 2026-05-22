namespace Bpm.Application.Sandbox;

/// <summary>
/// PR-J3: sandbox-only clock control. Advancing the offset shifts what every
/// downstream <c>IClock</c> consumer sees so SLA timers / due-date logic
/// behave as if days passed without the tester actually waiting.
/// </summary>
public interface ISandboxClockService
{
    Task<SandboxClockDto> GetAsync(CancellationToken ct = default);
    Task<SandboxClockDto> AdvanceAsync(int days, int hours, int minutes, int seconds, CancellationToken ct = default);
    Task<SandboxClockDto> ResetAsync(CancellationToken ct = default);
}

public sealed record SandboxClockDto(
    DateTime RealNow,
    DateTime SandboxNow,
    long OffsetSeconds,
    bool SandboxOn);

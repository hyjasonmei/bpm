namespace Bpm.ChefAgent;

/// <summary>
/// Pure cooldown arithmetic for noise control (env-down alerts, manual-merge
/// reminders). Kept separate so it's unit-testable without a clock dependency
/// — callers pass `now` and the last-sent time explicitly.
/// </summary>
public static class Cooldown
{
    /// <summary>True when no prior send exists, or enough time has elapsed.</summary>
    public static bool ShouldSend(DateTime now, DateTime? lastSentUtc, TimeSpan cooldown)
        => lastSentUtc is not { } last || now - last >= cooldown;
}

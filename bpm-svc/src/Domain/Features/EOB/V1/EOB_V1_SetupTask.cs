namespace Bpm.Domain.Features.EOB.V1;

/// <summary>One row inside the onboarding setup-checklist repeater
/// (門禁卡 / 帳號 / 資產 / 座位 / Mentor). Persisted as JSON.</summary>
public sealed record EOB_V1_SetupTask(
    string?  Task,
    string?  Status);   // Pending | Complete

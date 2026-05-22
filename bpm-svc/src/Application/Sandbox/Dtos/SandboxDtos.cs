namespace Bpm.Application.Sandbox.Dtos;

/// <summary>
/// Tenant-level sandbox configuration. Persisted as JSON inside
/// <c>TenantSettings.SandboxConfigJson</c> — new fields MUST stay optional with
/// sensible defaults so older tenant rows deserialize cleanly.
/// </summary>
/// <param name="EmailRecipients">Reserved for future "rewrite-and-deliver-to-test"
/// modes; unused by the capture-only gate. Kept on the DTO so existing tenant
/// JSON rows that still carry the field deserialize cleanly.</param>
/// <param name="WebhookUrl">Reserved (see <paramref name="EmailRecipients"/>).</param>
/// <param name="SmsRecipients">Reserved (see <paramref name="EmailRecipients"/>).</param>
/// <param name="CaptureRetentionDays">PR-J4 §7.6: how many days of captured
/// messages the cron job will keep before hard-deleting. Defaults to 30. The
/// cron itself is deferred — this field is wired into the DTO now so config
/// is forward-compatible when the SLA-timer/escalation PR adds the worker.</param>
public sealed record SandboxConfigDto(
    IReadOnlyList<string>? EmailRecipients,
    string? WebhookUrl,
    IReadOnlyList<string>? SmsRecipients,
    int CaptureRetentionDays = 30);

public sealed record SandboxStatusDto(
    bool Enabled,
    SandboxConfigDto? Config,
    DateTime? LastToggledAt,
    Guid? LastToggledByUserId);

public sealed record UpdateSandboxRequest(
    bool Enabled,
    SandboxConfigDto? Config);

/// <summary>
/// PR-J5 §10.1: surfaced for the bpm-ui RoleSwitcher's "sandbox personas"
/// dropdown. Any authenticated user can list personas — the act-as POST
/// (<see cref="SwitchPersonaRequest"/>) still requires admin role and a
/// sandbox-on tenant. Department name is denormalised here so the dropdown
/// can render without a second round-trip.
/// </summary>
public sealed record SandboxPersonaDto(
    Guid Id,
    string Email,
    string FullName,
    string? DepartmentName);

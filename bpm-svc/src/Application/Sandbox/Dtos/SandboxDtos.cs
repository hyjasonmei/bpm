using Bpm.Domain.Entities.Sandbox;

namespace Bpm.Application.Sandbox.Dtos;

/// <summary>
/// Tenant-level sandbox configuration. Persisted as JSON inside
/// <c>TenantSettings.SandboxConfigJson</c> — new fields MUST stay optional with
/// sensible defaults so older tenant rows deserialize cleanly.
/// </summary>
/// <param name="EmailRecipients">Optional alt recipients used by the legacy
/// rewrite path. Ignored when <paramref name="LegacyRewriteEnabled"/> is false
/// (the default capture-only mode).</param>
/// <param name="WebhookUrl">Optional alt webhook URL used by the legacy rewrite
/// path.</param>
/// <param name="SmsRecipients">Optional alt SMS recipients used by the legacy
/// rewrite path.</param>
/// <param name="LegacyRewriteEnabled">When true, the gate falls back to the
/// PR-J0 behavior — rewrite to alt recipients (or drop when empty) AND write
/// the legacy <c>SandboxRedirect</c> audit row. A capture row is still written
/// either way so the Mailbox UI is consistent. Defaults to false.</param>
/// <param name="CaptureRetentionDays">PR-J4 §7.6: how many days of captured
/// messages the cron job will keep before hard-deleting. Defaults to 30. The
/// cron itself is deferred — this field is wired into the DTO now so config
/// is forward-compatible when the SLA-timer/escalation PR adds the worker.</param>
public sealed record SandboxConfigDto(
    IReadOnlyList<string>? EmailRecipients,
    string? WebhookUrl,
    IReadOnlyList<string>? SmsRecipients,
    bool LegacyRewriteEnabled = false,
    int CaptureRetentionDays = 30);

public sealed record SandboxStatusDto(
    bool Enabled,
    SandboxConfigDto? Config,
    DateTime? LastToggledAt,
    Guid? LastToggledByUserId);

public sealed record UpdateSandboxRequest(
    bool Enabled,
    SandboxConfigDto? Config);

public sealed record SandboxRedirectDto(
    Guid Id,
    SandboxChannel Channel,
    SandboxAction Action,
    IReadOnlyList<string> OriginalTargets,
    IReadOnlyList<string> RedirectedTargets,
    string? SampleSubject,
    DateTime DispatchedAt);

using Bpm.Domain.Entities.Sandbox;

namespace Bpm.Application.Sandbox.Dtos;

public sealed record SandboxConfigDto(
    IReadOnlyList<string>? EmailRecipients,
    string? WebhookUrl,
    IReadOnlyList<string>? SmsRecipients);

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

namespace Bpm.Admin.Application.Flows;

/// <summary>One environment's deploy config — Azure resource NAMES only,
/// never secrets (Task 5).</summary>
public record DeployEnvConfigDto(
    string EnvName,
    string ResourceGroup,
    string BpmSvcApp,
    string AdminSvcApp,
    string BpmUiSwa,
    string AdminUiSwa,
    bool Enabled);

/// <summary>Upsert one env's config (PUT /api/site-setting/deploy-config).
/// Keyed by <see cref="EnvName"/>.</summary>
public record UpsertDeployEnvConfigRequest(
    string EnvName,
    string? ResourceGroup,
    string? BpmSvcApp,
    string? AdminSvcApp,
    string? BpmUiSwa,
    string? AdminUiSwa,
    bool Enabled);

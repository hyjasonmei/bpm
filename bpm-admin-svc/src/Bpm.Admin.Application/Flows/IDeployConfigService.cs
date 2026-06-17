namespace Bpm.Admin.Application.Flows;

/// <summary>
/// Reads / upserts per-environment deploy config for the Publish→Deploy
/// pipeline (Task 5). Stores Azure resource NAMES only — no secrets.
/// </summary>
public interface IDeployConfigService
{
    /// <summary>All env configs, ordered by env name.</summary>
    Task<IReadOnlyList<DeployEnvConfigDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Insert or update the row keyed by
    /// <see cref="UpsertDeployEnvConfigRequest.EnvName"/>.</summary>
    Task<DeployEnvConfigDto> UpsertAsync(UpsertDeployEnvConfigRequest req, CancellationToken ct = default);
}

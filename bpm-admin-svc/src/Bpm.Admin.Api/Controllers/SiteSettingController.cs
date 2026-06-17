using Bpm.Admin.Application.Flows;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

/// <summary>
/// Site-level admin settings. Currently exposes the per-environment deploy
/// config for the Publish→Deploy pipeline (Task 5) — Azure resource NAMES
/// only, never secrets.
///
/// Auth: relies on the global FallbackPolicy (RequireAuthenticatedUser) —
/// i.e. any authenticated admin JWT, matching the other admin write
/// endpoints (FlowGroups / Roles). No <c>[AllowAnonymous]</c>, so a
/// tokenless request 401s.
/// </summary>
[ApiController]
[Route("api/site-setting")]
public sealed class SiteSettingController : ControllerBase
{
    private readonly IDeployConfigService _deployConfig;

    public SiteSettingController(IDeployConfigService deployConfig)
    {
        _deployConfig = deployConfig;
    }

    [HttpGet("deploy-config")]
    public async Task<ActionResult<IReadOnlyList<DeployEnvConfigDto>>> GetDeployConfig(CancellationToken ct)
        => Ok(await _deployConfig.ListAsync(ct));

    [HttpPut("deploy-config")]
    public async Task<ActionResult<DeployEnvConfigDto>> UpsertDeployConfig(
        [FromBody] UpsertDeployEnvConfigRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.EnvName))
            return BadRequest("envName is required");
        var dto = await _deployConfig.UpsertAsync(req, ct);
        return Ok(dto);
    }
}

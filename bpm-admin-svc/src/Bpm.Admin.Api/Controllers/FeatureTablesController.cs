using System.Security.Claims;
using Bpm.Admin.Application.Flows;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/feature-tables")]
public sealed class FeatureTablesController : ControllerBase
{
    private readonly IFeatureTablesService _service;

    public FeatureTablesController(IFeatureTablesService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FeatureTableGroupDto>>> Scan(CancellationToken ct)
        => Ok(await _service.ScanAsync(ct));

    [HttpPost("archive")]
    public async Task<ActionResult<FeatureTableGroupDto>> Archive([FromBody] ArchiveFeatureRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.ArchiveAsync(req, CurrentUserId(), ct)); }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("restore")]
    public async Task<ActionResult<FeatureTableGroupDto>> Restore([FromBody] RestoreFeatureRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.RestoreAsync(req, CurrentUserId(), ct)); }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}

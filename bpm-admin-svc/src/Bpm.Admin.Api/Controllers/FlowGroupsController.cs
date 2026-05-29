using System.Security.Claims;
using Bpm.Admin.Application.Flows;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/flow-groups")]
public sealed class FlowGroupsController : ControllerBase
{
    private readonly IFlowGroupService _service;

    public FlowGroupsController(IFlowGroupService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FlowGroupDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<FlowGroupDto>> Create([FromBody] CreateFlowGroupRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateAsync(req, CurrentUserId(), ct)); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<FlowGroupDto>> Update(Guid id, [FromBody] UpdateFlowGroupRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.UpdateAsync(id, req, CurrentUserId(), ct)); }
        catch (FlowLifecycleException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await _service.DeleteAsync(id, CurrentUserId(), ct); return NoContent(); }
        catch (FlowLifecycleException ex) { return NotFound(ex.Message); }
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}

using System.Security.Claims;
using Bpm.Admin.Application.Flows;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/environments")]
public sealed class EnvironmentsController : ControllerBase
{
    private readonly IEnvironmentService _service;

    public EnvironmentsController(IEnvironmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<EnvironmentDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<EnvironmentDto>> Create([FromBody] CreateEnvironmentRequest req, CancellationToken ct)
    {
        try { return Ok(await _service.CreateAsync(req, CurrentUserId(), ct)); }
        catch (FlowLifecycleException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EnvironmentDto>> Update(Guid id, [FromBody] UpdateEnvironmentRequest req, CancellationToken ct)
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

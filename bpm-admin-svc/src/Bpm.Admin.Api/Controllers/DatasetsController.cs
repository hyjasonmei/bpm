using System.Security.Claims;
using Bpm.Admin.Application.Datasets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/datasets")]
public sealed class DatasetsController(IDatasetService svc) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<DatasetDto>>> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:guid}/rows")]
    public async Task<ActionResult<IEnumerable<DatasetRowDto>>> Rows(Guid id, CancellationToken ct)
        => Ok(await svc.ListRowsAsync(id, ct));

    [HttpPost, Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetDto>> Create([FromBody] CreateDatasetRequest req, CancellationToken ct)
    { try { return Ok(await svc.CreateAsync(req, Actor(), ct)); } catch (DatasetException e) { return BadRequest(e.Message); } }

    [HttpPut("{id:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetDto>> Update(Guid id, [FromBody] UpdateDatasetRequest req, CancellationToken ct)
    { try { return Ok(await svc.UpdateAsync(id, req, Actor(), ct)); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpDelete("{id:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { try { await svc.DeleteAsync(id, Actor(), ct); return NoContent(); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpPost("{id:guid}/rows"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetRowDto>> AddRow(Guid id, [FromBody] AddRowRequest req, CancellationToken ct)
    { try { return Ok(await svc.AddRowAsync(id, req, Actor(), ct)); } catch (DatasetException e) { return BadRequest(e.Message); } }

    [HttpPut("rows/{rowId:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<ActionResult<DatasetRowDto>> UpdateRow(Guid rowId, [FromBody] UpdateRowRequest req, CancellationToken ct)
    { try { return Ok(await svc.UpdateRowAsync(rowId, req, Actor(), ct)); } catch (DatasetException e) { return NotFound(e.Message); } }

    [HttpDelete("rows/{rowId:guid}"), Authorize(Policy = "SystemAdmin")]
    public async Task<IActionResult> DeleteRow(Guid rowId, CancellationToken ct)
    { try { await svc.DeleteRowAsync(rowId, Actor(), ct); return NoContent(); } catch (DatasetException e) { return NotFound(e.Message); } }

    private Guid? Actor()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var g) ? g : null;
    }
}

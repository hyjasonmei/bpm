// Interim controller. When add-process-runtime ships, these endpoints
// migrate to /api/process-instances and this file is removed.
// See openspec/changes/add-hr-flows-resign-deptx/proposal.md (sunset).

using Bpm.Application.Common.Exceptions;
using Bpm.Application.HrFlows;
using Bpm.Application.HrFlows.Dtos;
using Bpm.Domain.Entities.HrFlows;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.HrFlows;

[ApiController]
[Route("api/hr-flows")]
[Authorize]
public sealed class HrFlowsController(IHrFlowService service) : ControllerBase
{
    [HttpPost("{specCode}")]
    public async Task<IActionResult> Start(string specCode, [FromBody] StartHrFlowRequest req, CancellationToken ct)
    {
        if (!TryParseSpec(specCode, out var parsed))
            return BadRequest(new { error = "unsupported_spec_code", allowed = new[] { "RESIGN", "DEPTX" } });
        var userId = RequireUserId();
        var dto = await service.StartAsync(parsed, req.FormData, userId, ct);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpGet("mine")]
    public async Task<IReadOnlyList<HrFlowSummaryDto>> Mine(CancellationToken ct)
        => await service.GetMineAsync(RequireUserId(), ct);

    [HttpGet("todo")]
    public async Task<IReadOnlyList<HrFlowSummaryDto>> Todo(CancellationToken ct)
        => await service.GetMyTodoAsync(RequireUserId(), ct);

    [HttpGet("{id:guid}")]
    public async Task<HrFlowInstanceDto> GetById(Guid id, CancellationToken ct)
        => await service.GetByIdAsync(id, RequireUserId(), ct);

    [HttpPost("{id:guid}/approve")]
    public async Task<HrFlowInstanceDto> Approve(Guid id, [FromBody] ApproveRequest? req, CancellationToken ct)
        => await service.ApproveAsync(id, RequireUserId(), req?.Comment, ct);

    [HttpPost("{id:guid}/return")]
    public async Task<HrFlowInstanceDto> Return(Guid id, [FromBody] ReturnRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Comment))
            throw new ConflictException("return requires a non-empty comment");
        return await service.ReturnAsync(id, RequireUserId(), req.Comment, ct);
    }

    [HttpPost("{id:guid}/resubmit")]
    public async Task<HrFlowInstanceDto> Resubmit(Guid id, [FromBody] ResubmitRequest req, CancellationToken ct)
        => await service.ResubmitAsync(id, RequireUserId(), req.FormData, ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<HrFlowInstanceDto> Cancel(Guid id, CancellationToken ct)
        => await service.CancelAsync(id, RequireUserId(), ct);

    private static bool TryParseSpec(string raw, out HrFlowSpecCode parsed)
    {
        switch (raw?.ToUpperInvariant())
        {
            case "RESIGN": parsed = HrFlowSpecCode.Resign; return true;
            case "DEPTX": parsed = HrFlowSpecCode.Deptx; return true;
            default: parsed = default; return false;
        }
    }

    private Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}

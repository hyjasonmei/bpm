using Bpm.Api.Common;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Application.Process.Runtime.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Process;

[ApiController]
[Route("api/processes")]
[Authorize]
public sealed class ProcessController(
    IProcessRuntime runtime,
    IProcessQueryService query) : BpmControllerBase
{
    /// <summary>Start a new process instance from a spec code.</summary>
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartProcessRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.SpecCode))
            return BadRequest(new { error = "specCode is required" });

        var userId = RequireUserId();
        var result = await runtime.StartInstanceAsync(
            new StartInstanceCommand(req.SpecCode, req.FormData, userId), ct);

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.InstanceId },
            new { instanceId = result.InstanceId, firstTaskId = result.FirstTaskId });
    }

    [HttpGet("{id:guid}")]
    public async Task<ProcessInstanceDto> GetById(Guid id, CancellationToken ct)
        => await query.GetInstanceAsync(id, RequireUserId(), ct);

    [HttpGet("{id:guid}/history")]
    public async Task<HistoryPage> GetHistory(
        Guid id,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken ct)
        => await query.GetHistoryPageAsync(id, RequireUserId(), cursor, limit <= 0 ? 50 : limit, ct);

    /// <summary>
    /// PR-L3 — instances initiated by the calling user. Powers the Home
    /// "My recent cases" panel + the Search screen's user-scoped query.
    /// </summary>
    [HttpGet("mine")]
    public async Task<IReadOnlyList<MyInstanceSummaryDto>> Mine(
        [FromQuery] string? status,
        [FromQuery] int limit,
        CancellationToken ct)
        => await query.GetMyInstancesAsync(
            RequireUserId(),
            status ?? "all",
            limit <= 0 ? 50 : limit,
            ct);

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelProcessRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Reason))
            throw new ConflictException("cancel requires a non-empty reason");

        var userId = RequireUserId();
        await runtime.CancelInstanceAsync(new CancelInstanceCommand(id, userId, req.Reason), ct);
        return NoContent();
    }
}

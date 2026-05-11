using Bpm.Api.Common;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Domain.Entities.Process;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Process;

[ApiController]
[Route("api/tasks")]
[Authorize]
public sealed class TaskController(
    IProcessRuntime runtime,
    IProcessQueryService query) : BpmControllerBase
{
    [HttpGet("mine")]
    public async Task<IReadOnlyList<ProcessTaskDto>> Mine(
        [FromQuery] string? status,
        [FromQuery] int limit,
        CancellationToken ct)
        => await query.GetMineAsync(RequireUserId(), status ?? "open", limit <= 0 ? 50 : limit, ct);

    [HttpGet("{id:guid}")]
    public async Task<TaskWithFormDto> GetById(Guid id, CancellationToken ct)
        => await query.GetTaskAsync(id, RequireUserId(), ct);

    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id, CancellationToken ct)
    {
        await runtime.ClaimTaskAsync(new ClaimTaskCommand(id, RequireUserId()), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SubmitTaskRequest? req, CancellationToken ct)
    {
        Decision? decision = null;
        if (!string.IsNullOrWhiteSpace(req?.Decision))
        {
            if (!Enum.TryParse<Decision>(req.Decision, ignoreCase: true, out var parsed))
                throw new ConflictException($"unsupported decision '{req.Decision}' (allowed: Approve|Reject|Return)");
            decision = parsed;
        }

        await runtime.SubmitTaskAsync(new SubmitTaskCommand(
            id, RequireUserId(), req?.FormDataPatch, decision, req?.Comment), ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnTaskRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Comment))
            throw new ConflictException("return requires a non-empty comment");

        await runtime.ReturnTaskAsync(new ReturnTaskCommand(id, RequireUserId(), req.Comment), ct);
        return NoContent();
    }
}

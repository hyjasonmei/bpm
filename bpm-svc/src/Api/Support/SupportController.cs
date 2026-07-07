using Bpm.Application.Common.Exceptions;
using Bpm.Application.Support;
using Bpm.Domain.Entities.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Support;

public sealed record SetIssueStatusRequest(SupportIssueStatus Status);

[ApiController]
[Route("api/support/issues")]
[Authorize]
public sealed class SupportController(ISupportIssueService service) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitIssueRequest req, CancellationToken ct)
    {
        var ua = Request.Headers.UserAgent.ToString();
        var dto = await service.SubmitAsync(RequireUserId(), req, string.IsNullOrEmpty(ua) ? null : ua, ct);
        return Created($"/api/support/issues/{dto.Id}", dto);
    }

    [HttpGet]
    [Authorize(Roles = "SYSTEM_ADMIN")]
    public async Task<IReadOnlyList<IssueDto>> List([FromQuery] SupportIssueStatus? status, CancellationToken ct)
        => await service.ListAsync(status, ct);

    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "SYSTEM_ADMIN")]
    public async Task<IssueDto> SetStatus(Guid id, [FromBody] SetIssueStatusRequest req, CancellationToken ct)
        => await service.SetStatusAsync(id, req.Status, ct);

    private Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}

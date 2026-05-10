using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Sandbox;

[ApiController]
[Route("api/sandbox")]
[Authorize]
public sealed class SandboxController(ISandboxService service) : ControllerBase
{
    [HttpGet("status")]
    public async Task<SandboxStatusDto> Status(CancellationToken ct)
        => await service.GetStatusAsync(ct);

    [HttpPut("status")]
    [Authorize(Roles = "admin")]
    public async Task<SandboxStatusDto> SetStatus([FromBody] UpdateSandboxRequest req, CancellationToken ct)
        => await service.SetStatusAsync(req, RequireUserId(), ct);

    [HttpGet("redirects")]
    [Authorize(Roles = "admin")]
    public async Task<IReadOnlyList<SandboxRedirectDto>> Redirects([FromQuery] int days = 30, CancellationToken ct = default)
        => await service.GetRedirectsAsync(days, ct);

    private Guid RequireUserId()
    {
        var raw = User?.FindFirst("sub")?.Value
            ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User?.Identity?.Name;
        if (Guid.TryParse(raw, out var id)) return id;
        throw new ForbiddenException("authenticated user id missing or invalid");
    }
}

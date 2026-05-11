using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Sandbox;

[ApiController]
[Route("api/sandbox")]
[Authorize]
public sealed class SandboxController(
    ISandboxService service,
    ISandboxClockService clockService) : ControllerBase
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

    // ===== PR-J3 §4.4 — Sandbox clock advance/reset =====

    /// <summary>Open to any authenticated user; returns 0 offset when sandbox is off.</summary>
    [HttpGet("clock")]
    public async Task<SandboxClockDto> GetClock(CancellationToken ct)
        => await clockService.GetAsync(ct);

    [HttpPost("clock/advance")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AdvanceClock([FromBody] AdvanceClockRequest req, CancellationToken ct)
    {
        try
        {
            var dto = await clockService.AdvanceAsync(req.Days, req.Hours, req.Minutes, req.Seconds, ct);
            return Ok(dto);
        }
        catch (SandboxOffException)
        {
            return BadRequest(new { error = "sandbox_off" });
        }
    }

    [HttpPost("clock/reset")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetClock(CancellationToken ct)
    {
        try
        {
            var dto = await clockService.ResetAsync(ct);
            return Ok(dto);
        }
        catch (SandboxOffException)
        {
            return BadRequest(new { error = "sandbox_off" });
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

public sealed record AdvanceClockRequest(int Days = 0, int Hours = 0, int Minutes = 0, int Seconds = 0);

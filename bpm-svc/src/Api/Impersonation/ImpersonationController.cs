using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Impersonation;
using Bpm.Application.Impersonation.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Impersonation;

[ApiController]
[Route("api/impersonation")]
[Authorize]
public sealed class ImpersonationController(IImpersonationService service, ICurrentUser current) : ControllerBase
{
    public sealed record StartRequest(Guid TargetUserId, string Reason);

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartRequest req, CancellationToken ct)
    {
        var callerId = RequireUserId();
        var result = await service.StartAsync(callerId, req.TargetUserId, req.Reason ?? string.Empty,
            callerIsAlreadyImpersonating: current.ImpersonatedById is not null, ct);
        return Ok(result);
    }

    [HttpPost("end")]
    public async Task<IActionResult> End(CancellationToken ct)
    {
        if (current.ImpersonationSessionId is not Guid sessionId)
            return BadRequest(new { error = "not_in_impersonation" });
        // The caller is acting as `target`; their JWT has impersonated_by = original admin.
        // For end, we trust the session's impersonator id (so target can't end the admin's session arbitrarily).
        var byUser = current.ImpersonatedById ?? RequireUserId();
        await service.EndAsync(sessionId, byUser, ct);
        return NoContent();
    }

    [HttpGet("status")]
    public async Task<ImpersonationSessionDto?> Status(CancellationToken ct)
    {
        // Return active session for the *underlying* admin (impersonator). When not in impersonation,
        // for the caller themselves.
        var lookupUserId = current.ImpersonatedById ?? RequireUserId();
        return await service.GetActiveAsync(lookupUserId, ct);
    }

    [HttpGet("sessions")]
    public async Task<IReadOnlyList<ImpersonationSessionDto>> History([FromQuery] int days = 30, CancellationToken ct = default)
        => await service.GetHistoryAsync(days, ct);

    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await service.RevokeAsync(id, RequireUserId(), ct);
        return NoContent();
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

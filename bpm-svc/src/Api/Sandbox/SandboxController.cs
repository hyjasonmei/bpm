using Bpm.Api.Auth;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Sandbox;

[ApiController]
[Route("api/sandbox")]
[Authorize]
public sealed class SandboxController(
    ISandboxService service,
    ISandboxClockService clockService,
    IResetService resetService,
    IMailboxService mailbox,
    AppDbContext db,
    JwtTokenService jwt) : ControllerBase
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

    // ===== PR-J4 §6 — Sandbox persona switch =====

    /// <summary>
    /// Mints a sandbox-persona JWT so an admin tester can act-as another
    /// seed user without leaving an audit gap — the audit interceptor
    /// stamps <c>SandboxActualActor</c> on every row the persona writes.
    /// </summary>
    [HttpPost("persona")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SwitchPersona([FromBody] SwitchPersonaRequest req, CancellationToken ct)
    {
        // Sandbox-on gate first — the persona switch is intentionally a
        // sandbox-only feature so prod can't accidentally hand out act-as
        // tokens just because someone has admin role.
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return BadRequest(new { error = "sandbox_off" });

        var actor = RequireUserId();
        var actorEmail = User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value
            ?? "unknown@sandbox";

        var persona = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
        if (persona is null)
            return NotFound(new { error = "user_not_found", userId = req.UserId });

        // Resolve persona's role codes the same way DevLogin does — go through
        // RoleAssignment + Role tables. Inactive role assignments are skipped.
        var roleCodes = await (
            from ra in db.RoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on ra.RoleId equals r.Id
            where ra.PrincipalId == persona.Id
            select r.Code).Distinct().ToListAsync(ct);

        var (token, expiresAt) = jwt.IssueSandboxPersonaToken(
            personaUserId: persona.Id,
            personaEmail: persona.Email,
            personaFullName: persona.FullName,
            personaRoles: roleCodes,
            actualActorUserId: actor,
            actualActorEmail: actorEmail);

        return Ok(new
        {
            token,
            expiresAt,
            persona = new { id = persona.Id, email = persona.Email, fullName = persona.FullName, roles = roleCodes },
            actualActor = new { id = actor, email = actorEmail },
        });
    }

    // ===== PR-J4 §7 — Mailbox API =====

    /// <summary>List captured messages with optional filters.</summary>
    [HttpGet("captured")]
    public async Task<IActionResult> ListCaptured(
        [FromQuery] string? channel,
        [FromQuery] Guid? recipientUserId,
        [FromQuery] Guid? processInstanceId,
        [FromQuery] bool unread = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        // §7.5: refuse with 403 when sandbox off so prod can't enumerate
        // historical captures (and the SandboxBanner unread-count poll is
        // the ONLY endpoint that returns a silent zero in that case).
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return StatusCode(StatusCodes.Status403Forbidden, new { error = "sandbox_off" });

        SandboxChannel? channelEnum = null;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!Enum.TryParse<SandboxChannel>(channel, ignoreCase: true, out var parsed))
                return BadRequest(new { error = "invalid_channel", channel });
            channelEnum = parsed;
        }

        var rows = await mailbox.ListAsync(
            currentUserId: RequireUserId(),
            channel: channelEnum,
            recipientUserIdHint: recipientUserId,
            processInstanceId: processInstanceId,
            unreadOnly: unread,
            limit: limit,
            ct);
        return Ok(rows);
    }

    [HttpGet("captured/{id:guid}")]
    public async Task<IActionResult> GetCaptured(Guid id, CancellationToken ct)
    {
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return StatusCode(StatusCodes.Status403Forbidden, new { error = "sandbox_off" });

        var dto = await mailbox.GetAsync(id, RequireUserId(), ct);
        if (dto is null) return NotFound(new { error = "not_found", id });
        return Ok(dto);
    }

    [HttpPost("captured/{id:guid}/read")]
    public async Task<IActionResult> MarkCapturedRead(Guid id, CancellationToken ct)
    {
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return StatusCode(StatusCodes.Status403Forbidden, new { error = "sandbox_off" });

        var ok = await mailbox.MarkReadAsync(id, RequireUserId(), ct);
        if (!ok) return NotFound(new { error = "not_found", id });
        return Ok(new { id, readByMe = true });
    }

    /// <summary>
    /// Counter for the SandboxBanner badge. Returns zero counts WITHOUT a DB
    /// hit when sandbox is off (silent zero, NOT 403) so the banner poll is
    /// safe in prod.
    /// </summary>
    [HttpGet("captured/unread-count")]
    public async Task<UnreadCountDto> UnreadCount(CancellationToken ct)
        => await mailbox.UnreadCountAsync(RequireUserId(), ct);

    // ===== PR-J4 §8.4-8.5 — Reset endpoints =====

    [HttpPost("reset/instance/{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetInstance(Guid id, CancellationToken ct)
    {
        try
        {
            var summary = await resetService.ResetInstanceAsync(id, RequireUserId(), ct);
            return Ok(summary);
        }
        catch (SandboxOffException)
        {
            return BadRequest(new { error = "sandbox_off" });
        }
    }

    [HttpPost("reset/all")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetAll(CancellationToken ct)
    {
        try
        {
            var summary = await resetService.ResetAllAsync(RequireUserId(), ct);
            return Ok(summary);
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

public sealed record SwitchPersonaRequest(Guid UserId);

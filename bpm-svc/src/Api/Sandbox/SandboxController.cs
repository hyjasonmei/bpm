using Bpm.Api.Auth;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
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
    public async Task<IActionResult> SetStatus([FromBody] UpdateSandboxRequest req, CancellationToken ct)
    {
        // PR-J5 §12.4: defense-in-depth for prod deploys — when the operator
        // sets BPM_SANDBOX_TOGGLE_DISABLED=true, no one (not even admin) can
        // flip sandbox on/off via the API. Useful when sandbox state is
        // managed out-of-band (CD pipeline / config-management) and the
        // self-serve toggle would be a liability.
        if (IsToggleDisabledByEnv())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "sandbox_toggle_disabled" });
        var result = await service.SetStatusAsync(req, RequireUserId(), ct);
        return Ok(result);
    }

    private static bool IsToggleDisabledByEnv()
    {
        var raw = Environment.GetEnvironmentVariable("BPM_SANDBOX_TOGGLE_DISABLED");
        return !string.IsNullOrEmpty(raw)
            && (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1");
    }

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
    [Authorize(Roles = "Persona_Switch,SystemAdmin,admin")]
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

        var persona = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.UserId && u.Type == SharedPrincipalType.User, ct);
        if (persona is null)
            return NotFound(new { error = "user_not_found", userId = req.UserId });

        // Resolve persona's role names via the unified Admin_PrincipalRoles + Admin_Roles.
        var roleNames = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            where pr.PrincipalId == persona.Id
            select r.Name).Distinct().ToListAsync(ct);

        var (token, expiresAt) = jwt.IssueSandboxPersonaToken(
            personaUserId: persona.Id,
            personaEmail: persona.Email ?? string.Empty,
            personaFullName: persona.DisplayName,
            personaRoles: roleNames,
            actualActorUserId: actor,
            actualActorEmail: actorEmail);

        return Ok(new
        {
            token,
            expiresAt,
            persona = new { id = persona.Id, email = persona.Email, fullName = persona.DisplayName, roles = roleNames },
            actualActor = new { id = actor, email = actorEmail },
        });
    }

    /// <summary>
    /// PR-J5 §10.1: list users available as sandbox personas for the
    /// RoleSwitcher dropdown in bpm-ui. Open to any authenticated user
    /// because the read carries no privilege — the actual act-as is gated
    /// by <c>POST /api/sandbox/persona</c> (admin + sandbox-on). Returns
    /// silent empty list when sandbox is off so the dropdown can poll
    /// without a 403 noise.
    /// </summary>
    [HttpGet("personas")]
    public async Task<IReadOnlyList<SandboxPersonaDto>> ListPersonas(CancellationToken ct)
    {
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return Array.Empty<SandboxPersonaDto>();

        var users = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null)
            .OrderBy(u => u.DisplayName)
            .Take(200)
            .ToListAsync(ct);

        var rows = new List<SandboxPersonaDto>(users.Count);
        foreach (var u in users)
        {
            var deptName = await (
                from ud in db.SharedUserDepts.AsNoTracking()
                where ud.UserId == u.Id && ud.IsPrimary
                join d in db.SharedPrincipals.AsNoTracking() on ud.DeptId equals d.Id
                select (string?)d.DisplayName).FirstOrDefaultAsync(ct);
            rows.Add(new SandboxPersonaDto(u.Id, u.Email ?? string.Empty, u.DisplayName, deptName));
        }
        return rows;
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
            flowCode: null,
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

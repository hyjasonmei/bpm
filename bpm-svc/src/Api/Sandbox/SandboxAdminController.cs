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

/// <summary>
/// Console-facing sandbox surface for the admin-ui Sandbox page. Unlike the
/// bearer-gated <see cref="SandboxController"/> (used by bpm-ui, which carries a
/// JWT), this controller is reached by admin-ui through its <c>/bpmsvc</c> dev
/// proxy with no bpm-svc bearer — so it is <c>[AllowAnonymous]</c> and never
/// reads JWT claims, mirroring the branding / reports / flow-codes controllers.
///
/// <para>POC deferral: the real admin↔bpm auth bridge lands later. Actor
/// attribution falls back to a sentinel where the underlying service records
/// one (sandbox toggle / reset write Info logs only).</para>
/// </summary>
[ApiController]
[Route("api/sandbox-admin")]
[AllowAnonymous]
public sealed class SandboxAdminController(
    ISandboxService service,
    ISandboxClockService clockService,
    IResetService resetService,
    IMailboxService mailbox,
    IFlowSandboxConfigService flowConfig,
    AppDbContext db,
    JwtTokenService jwt) : ControllerBase
{
    // ===== Global toggle =====

    [HttpGet("status")]
    public Task<SandboxStatusDto> Status(CancellationToken ct) => service.GetStatusAsync(ct);

    [HttpPut("status")]
    public async Task<SandboxStatusDto> SetStatus([FromBody] SetSandboxStatusRequest req, CancellationToken ct)
        => await service.SetStatusAsync(new UpdateSandboxRequest(req.Enabled, null), Guid.Empty, ct);

    // ===== Clock =====

    [HttpGet("clock")]
    public Task<SandboxClockDto> GetClock(CancellationToken ct) => clockService.GetAsync(ct);

    [HttpPost("clock/advance")]
    public async Task<IActionResult> AdvanceClock([FromBody] AdvanceClockRequest req, CancellationToken ct)
    {
        try { return Ok(await clockService.AdvanceAsync(req.Days, req.Hours, req.Minutes, req.Seconds, ct)); }
        catch (SandboxOffException) { return BadRequest(new { error = "sandbox_off" }); }
    }

    [HttpPost("clock/reset")]
    public async Task<IActionResult> ResetClock(CancellationToken ct)
    {
        try { return Ok(await clockService.ResetAsync(ct)); }
        catch (SandboxOffException) { return BadRequest(new { error = "sandbox_off" }); }
    }

    // ===== Persona =====

    /// <summary>List candidate personas. Unlike the bpm-ui endpoint this does
    /// NOT gate on sandbox-on, so the operator can pick a persona before
    /// flipping sandbox.</summary>
    [HttpGet("personas")]
    public async Task<IReadOnlyList<SandboxPersonaDto>> ListPersonas(CancellationToken ct)
    {
        var users = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null)
            .OrderBy(u => u.DisplayName)
            .Take(200)
            .ToListAsync(ct);
        return users
            .Select(u => new SandboxPersonaDto(u.Id, u.Email ?? string.Empty, u.DisplayName, null))
            .ToList();
    }

    /// <summary>Mint a sandbox-persona token to hand to bpm-ui. Sandbox must be
    /// on (the persona token is a sandbox-only act-as).</summary>
    [HttpPost("persona")]
    public async Task<IActionResult> SwitchPersona([FromBody] ConsolePersonaRequest req, CancellationToken ct)
    {
        var clock = await clockService.GetAsync(ct);
        if (!clock.SandboxOn) return BadRequest(new { error = "sandbox_off" });

        var persona = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.UserId && u.Type == SharedPrincipalType.User, ct);
        if (persona is null) return NotFound(new { error = "user_not_found", userId = req.UserId });

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
            actualActorUserId: req.ActualActorUserId ?? Guid.Empty,
            actualActorEmail: req.ActualActorEmail ?? "sandbox-console");

        return Ok(new
        {
            token,
            expiresAt,
            persona = new { id = persona.Id, email = persona.Email, fullName = persona.DisplayName, roles = roleNames },
        });
    }

    // ===== Mailbox (capture) =====

    [HttpGet("captured")]
    public async Task<IActionResult> ListCaptured(
        [FromQuery] string? flowCode,
        [FromQuery] string? channel,
        [FromQuery] bool unread = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        SandboxChannel? channelEnum = null;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!Enum.TryParse<SandboxChannel>(channel, ignoreCase: true, out var parsed))
                return BadRequest(new { error = "invalid_channel", channel });
            channelEnum = parsed;
        }

        var rows = await mailbox.ListAsync(
            currentUserId: Guid.Empty,
            channel: channelEnum,
            recipientUserIdHint: null,
            processInstanceId: null,
            flowCode: flowCode,
            unreadOnly: unread,
            limit: limit,
            ct);
        return Ok(rows);
    }

    [HttpGet("captured/{id:guid}")]
    public async Task<IActionResult> GetCaptured(Guid id, CancellationToken ct)
    {
        var dto = await mailbox.GetAsync(id, Guid.Empty, ct);
        if (dto is null) return NotFound(new { error = "not_found", id });
        return Ok(dto);
    }

    // ===== Per-flow scope =====

    [HttpGet("flows")]
    public Task<IReadOnlyList<FlowSandboxStateDto>> ListFlows(CancellationToken ct) => flowConfig.ListAsync(ct);

    [HttpPut("flows/{flowCode}")]
    public Task<FlowSandboxStateDto> SetFlowCapture(string flowCode, [FromBody] SetFlowCaptureRequest req, CancellationToken ct)
        => flowConfig.SetCaptureAsync(flowCode, req.Enabled, ct);

    // ===== Reset =====

    [HttpPost("reset/all")]
    public async Task<IActionResult> ResetAll(CancellationToken ct)
    {
        try { return Ok(await resetService.ResetAllAsync(Guid.Empty, ct)); }
        catch (SandboxOffException) { return BadRequest(new { error = "sandbox_off" }); }
    }

    [HttpPost("reset/flow/{flowCode}")]
    public async Task<IActionResult> ResetFlow(string flowCode, CancellationToken ct)
    {
        try { return Ok(await resetService.ResetFlowAsync(flowCode, Guid.Empty, ct)); }
        catch (SandboxOffException) { return BadRequest(new { error = "sandbox_off" }); }
    }
}

public sealed record SetSandboxStatusRequest(bool Enabled);
public sealed record SetFlowCaptureRequest(bool Enabled);
public sealed record ConsolePersonaRequest(Guid UserId, Guid? ActualActorUserId = null, string? ActualActorEmail = null);

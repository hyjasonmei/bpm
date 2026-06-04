using Bpm.Api.Common;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Delegation;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Delegation;

/// <summary>
/// End-user self-service delegation (代理人) for the bpm app. Bearer-authed —
/// the caller is the delegator. Writes the shared <c>Admin_Delegations</c> table
/// (single source the runtime + inbox + decision-auth all honor).
/// </summary>
[ApiController]
[Route("api/delegation")]
[Authorize]
public sealed class DelegationController(AppDbContext db, IDelegationService delegation, IClock clock) : BpmControllerBase
{
    [HttpGet("mine")]
    public async Task<MyDelegationDto?> GetMine(CancellationToken ct)
    {
        var me = RequireUserId();
        var d = await db.SharedDelegations.AsNoTracking()
            .Where(x => x.DelegatorPrincipalId == me && x.Active)
            .OrderByDescending(x => x.StartAt).FirstOrDefaultAsync(ct);
        if (d is null) return null;
        var name = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Id == d.DelegateToUserId).Select(p => p.DisplayName).FirstOrDefaultAsync(ct);
        var now = clock.UtcNow;
        return new MyDelegationDto(d.Id, d.DelegateToUserId, name, d.StartAt, d.EndAt, d.Active && d.StartAt <= now && d.EndAt >= now);
    }

    [HttpPut("mine")]
    public async Task<IActionResult> SetMine([FromBody] SetDelegationRequest req, CancellationToken ct)
    {
        var me = RequireUserId();
        if (req.DelegateUserId == me) return BadRequest(new { error = "cannot_delegate_to_self" });
        if (req.EndAt < req.StartAt) return BadRequest(new { error = "end_before_start" });

        var target = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == req.DelegateUserId && p.Type == SharedPrincipalType.User, ct);
        if (target is null) return NotFound(new { error = "delegate_not_found" });

        var now = clock.UtcNow;
        var existing = await db.SharedDelegations.FirstOrDefaultAsync(x => x.DelegatorPrincipalId == me && x.Active, ct);
        if (existing is null)
        {
            db.SharedDelegations.Add(new SharedDelegation
            {
                Id = Guid.NewGuid(),
                DelegatorPrincipalId = me,
                DelegateToUserId = req.DelegateUserId,
                StartAt = req.StartAt,
                EndAt = req.EndAt,
                Active = true,
                Reason = req.Reason,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.DelegateToUserId = req.DelegateUserId;
            existing.StartAt = req.StartAt;
            existing.EndAt = req.EndAt;
            existing.Active = true;
            existing.Reason = req.Reason;
            existing.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpDelete("mine")]
    public async Task<IActionResult> ClearMine(CancellationToken ct)
    {
        var me = RequireUserId();
        var rows = await db.SharedDelegations.Where(x => x.DelegatorPrincipalId == me && x.Active).ToListAsync(ct);
        foreach (var r in rows) { r.Active = false; r.UpdatedAt = clock.UtcNow; }
        await db.SaveChangesAsync(ct);
        return Ok(new { ok = true, cleared = rows.Count });
    }

    /// <summary>User ids the caller may currently act on behalf of (drives the
    /// case-detail "can act" check + the inbox fan-in tag).</summary>
    [HttpGet("acting-for")]
    public async Task<IReadOnlyList<Guid>> ActingFor(CancellationToken ct)
        => await delegation.GetActiveDelegatorsAsync(RequireUserId(), clock.UtcNow, ct);

    [HttpGet("users")]
    public async Task<IReadOnlyList<DelegationUserDto>> Users(CancellationToken ct)
    {
        var me = RequireUserId();
        return await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null && p.Id != me)
            .OrderBy(p => p.DisplayName).Take(300)
            .Select(p => new DelegationUserDto(p.Id, p.DisplayName, p.Email)).ToListAsync(ct);
    }
}

public sealed record MyDelegationDto(Guid Id, Guid DelegateUserId, string? DelegateName, DateTime StartAt, DateTime EndAt, bool ActiveNow);
public sealed record SetDelegationRequest(Guid DelegateUserId, DateTime StartAt, DateTime EndAt, string? Reason = null);
public sealed record DelegationUserDto(Guid UserId, string Name, string? Email);

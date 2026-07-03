using Bpm.Api.Common;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Delegation;
using Bpm.Application.Notifications;
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
///
/// A delegation must be ACCEPTED by the delegate before it takes effect
/// (Status Pending → Accepted/Declined). Re-designating supersedes the current
/// active row (kept as history) and starts a fresh Pending one.
/// </summary>
[ApiController]
[Route("api/delegation")]
[Authorize]
public sealed class DelegationController(AppDbContext db, IDelegationService delegation, IClock clock, INotifyDispatcher notify) : BpmControllerBase
{
    private const int Pending = (int)DelegationStatus.Pending;
    private const int Accepted = (int)DelegationStatus.Accepted;
    private const int Declined = (int)DelegationStatus.Declined;

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
        var effective = d.Active && d.Status == Accepted && d.StartAt <= now && d.EndAt >= now;
        return new MyDelegationDto(d.Id, d.DelegateToUserId, name, d.StartAt, d.EndAt, effective, StatusName(d.Status));
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
        // Supersede any current active row (kept as history) and start a fresh
        // Pending delegation — the delegate must accept before it takes effect.
        var existing = await db.SharedDelegations.Where(x => x.DelegatorPrincipalId == me && x.Active).ToListAsync(ct);
        foreach (var e in existing) { e.Active = false; e.UpdatedAt = now; }

        var row = new SharedDelegation
        {
            Id = Guid.NewGuid(),
            DelegatorPrincipalId = me,
            DelegateToUserId = req.DelegateUserId,
            StartAt = req.StartAt,
            EndAt = req.EndAt,
            Active = true,
            Status = Pending,
            RespondedAt = null,
            Reason = req.Reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SharedDelegations.Add(row);
        await db.SaveChangesAsync(ct);

        await NotifyDesignatedAsync(row, ct);
        return Ok(new { ok = true, status = "Pending" });
    }

    /// <summary>Delegations awaiting THIS user's accept/decline (they were designated).</summary>
    [HttpGet("pending-mine")]
    public async Task<IReadOnlyList<PendingDelegationDto>> PendingMine(CancellationToken ct)
    {
        var me = RequireUserId();
        var rows = await db.SharedDelegations.AsNoTracking()
            .Where(x => x.DelegateToUserId == me && x.Active && x.Status == Pending)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<PendingDelegationDto>();
        var names = await db.SharedPrincipals.AsNoTracking()
            .Where(p => rows.Select(r => r.DelegatorPrincipalId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        return rows.Select(r => new PendingDelegationDto(
            r.Id, r.DelegatorPrincipalId, names.GetValueOrDefault(r.DelegatorPrincipalId),
            r.StartAt, r.EndAt, r.Reason)).ToList();
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
        => await RespondAsync(id, accept: true, ct);

    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> Decline(Guid id, CancellationToken ct)
        => await RespondAsync(id, accept: false, ct);

    private async Task<IActionResult> RespondAsync(Guid id, bool accept, CancellationToken ct)
    {
        var me = RequireUserId();
        var row = await db.SharedDelegations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(new { error = "delegation_not_found" });
        if (row.DelegateToUserId != me) return Forbid();
        if (!row.Active || row.Status != Pending) return Conflict(new { error = "not_pending" });

        var now = clock.UtcNow;
        row.RespondedAt = now;
        row.UpdatedAt = now;
        if (accept) { row.Status = Accepted; }
        else { row.Status = Declined; row.Active = false; }   // declined kept as history (D1=a)
        await db.SaveChangesAsync(ct);

        await NotifyResponseToDelegatorAsync(row, accept, ct);
        return Ok(new { ok = true, status = accept ? "Accepted" : "Declined" });
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

    /// <summary>Same set as acting-for, but with the delegator's display name so
    /// the bpm-ui can surface a "你目前是 X 的代理人" banner. Additive — acting-for
    /// stays id-only for the case-detail can-act check.</summary>
    [HttpGet("acting-for-detail")]
    public async Task<IReadOnlyList<ActingForDto>> ActingForDetail(CancellationToken ct)
    {
        var me = RequireUserId();
        var ids = await delegation.GetActiveDelegatorsAsync(me, clock.UtcNow, ct);
        if (ids.Count == 0) return Array.Empty<ActingForDto>();
        var names = await db.SharedPrincipals.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        return ids.Select(id => new ActingForDto(id, names.GetValueOrDefault(id))).ToList();
    }

    /// <summary>
    /// Server-side typeahead for the delegate picker — scales to thousands of
    /// users since the client never fetches the full directory. Filters by name
    /// or email substring (LIKE; portable SQLite/Postgres), returns the top
    /// <paramref name="limit"/> matches. (For very large directories swap the
    /// LIKE for the ISearchService FTS path per the DB conventions.)
    /// </summary>
    [HttpGet("users")]
    public async Task<IReadOnlyList<DelegationUserDto>> Users([FromQuery] string? q, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var me = RequireUserId();
        limit = Math.Clamp(limit, 1, 50);

        var query = db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.Active && p.DeletedAt == null && p.Id != me);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p =>
                EF.Functions.Like(p.DisplayName, "%" + s + "%") ||
                (p.Email != null && EF.Functions.Like(p.Email, "%" + s + "%")));
        }
        return await query.OrderBy(p => p.DisplayName).Take(limit)
            .Select(p => new DelegationUserDto(p.Id, p.DisplayName, p.Email)).ToListAsync(ct);
    }

    private static string StatusName(int status) => status switch
    {
        Accepted => "Accepted",
        Declined => "Declined",
        _ => "Pending",
    };

    private async Task NotifyDesignatedAsync(SharedDelegation row, CancellationToken ct)
    {
        var lookups = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Id == row.DelegatorPrincipalId || p.Id == row.DelegateToUserId)
            .ToDictionaryAsync(p => p.Id, p => new { p.DisplayName, p.Email }, ct);
        var delegator = lookups.GetValueOrDefault(row.DelegatorPrincipalId);
        var delegate_ = lookups.GetValueOrDefault(row.DelegateToUserId);
        var who = delegator?.DisplayName ?? "某位同事";
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: "delegation.designated",
            Subject: $"【待回應】{who} 指定你為代理人",
            Body: $"{who} 指定你於 {row.StartAt:yyyy-MM-dd} ~ {row.EndAt:yyyy-MM-dd} 期間代理其待辦。請至「代理人」設定接受或拒絕。",
            Channels: new[] { "in_app" },
            Recipients: new[] { new NotifyRecipient(row.DelegateToUserId, delegate_?.Email, delegate_?.DisplayName) },
            Context: new Dictionary<string, string?> { ["delegationId"] = row.Id.ToString(), ["kind"] = "designated" }), ct);
    }

    private async Task NotifyResponseToDelegatorAsync(SharedDelegation row, bool accepted, CancellationToken ct)
    {
        var lookups = await db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Id == row.DelegatorPrincipalId || p.Id == row.DelegateToUserId)
            .ToDictionaryAsync(p => p.Id, p => new { p.DisplayName, p.Email }, ct);
        var delegator = lookups.GetValueOrDefault(row.DelegatorPrincipalId);
        var delegate_ = lookups.GetValueOrDefault(row.DelegateToUserId);
        var who = delegate_?.DisplayName ?? "對方";
        var verb = accepted ? "已接受" : "已拒絕";
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: accepted ? "delegation.accepted" : "delegation.declined",
            Subject: $"【代理人{verb}】{who} {verb}你的代理指定",
            Body: accepted
                ? $"{who} 已接受成為你的代理人（{row.StartAt:yyyy-MM-dd} ~ {row.EndAt:yyyy-MM-dd}），代理已生效。"
                : $"{who} 拒絕了你的代理指定。請重新指定其他代理人。",
            Channels: new[] { "in_app" },
            Recipients: new[] { new NotifyRecipient(row.DelegatorPrincipalId, delegator?.Email, delegator?.DisplayName) },
            Context: new Dictionary<string, string?> { ["delegationId"] = row.Id.ToString(), ["kind"] = accepted ? "accepted" : "declined" }), ct);
    }
}

public sealed record MyDelegationDto(Guid Id, Guid DelegateUserId, string? DelegateName, DateTime StartAt, DateTime EndAt, bool ActiveNow, string Status);
public sealed record PendingDelegationDto(Guid Id, Guid DelegatorUserId, string? DelegatorName, DateTime StartAt, DateTime EndAt, string? Reason);
public sealed record SetDelegationRequest(Guid DelegateUserId, DateTime StartAt, DateTime EndAt, string? Reason = null);
public sealed record DelegationUserDto(Guid UserId, string Name, string? Email);
public sealed record ActingForDto(Guid DelegatorUserId, string? DelegatorName);

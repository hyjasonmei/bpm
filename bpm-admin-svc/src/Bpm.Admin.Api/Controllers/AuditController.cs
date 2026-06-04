using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

/// <summary>
/// Read-only viewer over the append-only Admin_AuditEvents ledger. Lists
/// events newest-first with server-side filtering + paging, and resolves the
/// actor's display name so the UI shows a person, not a UUID.
/// </summary>
[ApiController]
[Route("api/audit-events")]
public class AuditController : ControllerBase
{
    private readonly AdminDbContext _db;

    public AuditController(AdminDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<AuditPageDto>> List(
        [FromQuery] string? actionType,
        [FromQuery] string? targetType,
        [FromQuery] string? source,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var q = _db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(actionType)) q = q.Where(e => e.ActionType == actionType);
        if (!string.IsNullOrWhiteSpace(targetType)) q = q.Where(e => e.TargetType == targetType);
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(e => e.SourceSystem == source);
        if (actorUserId is { } a) q = q.Where(e => e.ActorUserId == a);
        if (from is { } f) q = q.Where(e => e.Timestamp >= f);
        if (to is { } t) q = q.Where(e => e.Timestamp <= t);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e =>
                e.ActionType.Contains(s) ||
                e.TargetType.Contains(s) ||
                (e.TargetId != null && e.TargetId.Contains(s)) ||
                (e.Reason != null && e.Reason.Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(e => e.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        // Resolve actor display names in one round-trip.
        var actorIds = rows.Where(r => r.ActorUserId is not null)
            .Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var names = actorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Principals.AsNoTracking()
                .Where(p => actorIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        var items = rows.Select(e => new AuditEventDto(
            EventId: e.EventId,
            Timestamp: e.Timestamp,
            ActionType: e.ActionType,
            TargetType: e.TargetType,
            TargetId: e.TargetId,
            ActorUserId: e.ActorUserId,
            ActorDisplayName: e.ActorUserId is { } id ? names.GetValueOrDefault(id) : null,
            SourceSystem: e.SourceSystem,
            Reason: e.Reason,
            BeforeJson: e.BeforeJson,
            AfterJson: e.AfterJson)).ToList();

        return Ok(new AuditPageDto(items, total));
    }

    /// <summary>Distinct values powering the filter dropdowns, so the UI never
    /// offers an action / target / source that has no events.</summary>
    [HttpGet("facets")]
    public async Task<ActionResult<AuditFacetsDto>> Facets(CancellationToken ct)
    {
        var actionTypes = await _db.AuditEvents.AsNoTracking()
            .Select(e => e.ActionType).Distinct().OrderBy(a => a).ToListAsync(ct);
        var targetTypes = await _db.AuditEvents.AsNoTracking()
            .Select(e => e.TargetType).Distinct().OrderBy(t => t).ToListAsync(ct);
        var sources = await _db.AuditEvents.AsNoTracking()
            .Select(e => e.SourceSystem).Distinct().OrderBy(s => s).ToListAsync(ct);
        return Ok(new AuditFacetsDto(actionTypes, targetTypes, sources));
    }
}

public sealed record AuditEventDto(
    Guid EventId,
    DateTime Timestamp,
    string ActionType,
    string TargetType,
    string? TargetId,
    Guid? ActorUserId,
    string? ActorDisplayName,
    string SourceSystem,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

public sealed record AuditPageDto(IReadOnlyList<AuditEventDto> Items, int Total);

public sealed record AuditFacetsDto(
    IReadOnlyList<string> ActionTypes,
    IReadOnlyList<string> TargetTypes,
    IReadOnlyList<string> Sources);

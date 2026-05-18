using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Delegations;
using Bpm.Admin.Domain.Delegations;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/delegations")]
public class DelegationsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public DelegationsController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DelegationDto>>> List(
        [FromQuery] Guid? delegatorPrincipalId,
        [FromQuery] bool? onlyActive,
        CancellationToken ct)
    {
        var q = _db.Delegations.AsQueryable();
        if (delegatorPrincipalId.HasValue) q = q.Where(d => d.DelegatorPrincipalId == delegatorPrincipalId.Value);
        if (onlyActive == true)
        {
            var now = DateTime.UtcNow;
            q = q.Where(d => d.Active && d.StartAt <= now && d.EndAt >= now);
        }
        var rows = await q.OrderBy(d => d.StartAt).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<DelegationDto>> Create([FromBody] CreateDelegationRequest req, CancellationToken ct)
    {
        if (req.EndAt <= req.StartAt) return BadRequest("EndAt must be after StartAt.");

        var delegateToPrincipal = await _db.Principals
            .FirstOrDefaultAsync(p => p.Id == req.DelegateToUserId, ct);
        if (delegateToPrincipal is null) return BadRequest("DelegateToUser principal does not exist.");
        if (delegateToPrincipal.Type != PrincipalType.User)
            return BadRequest("Delegation target must be a user.");

        if (!await _db.Principals.AnyAsync(p => p.Id == req.DelegatorPrincipalId, ct))
            return BadRequest("Delegator principal does not exist.");

        var d = new Delegation
        {
            Id = Guid.NewGuid(),
            DelegatorPrincipalId = req.DelegatorPrincipalId,
            DelegateToUserId = req.DelegateToUserId,
            StartAt = req.StartAt,
            EndAt = req.EndAt,
            Reason = req.Reason,
            Active = true,
        };
        _db.Delegations.Add(d);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "created",
            targetType: "delegation",
            targetId: d.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            after: ToDto(d),
            ct: ct);

        return Created($"/api/delegations/{d.Id}", ToDto(d));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var d = await _db.Delegations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var before = ToDto(d);
        d.Active = false;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "cancelled",
            targetType: "delegation",
            targetId: d.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: ToDto(d),
            ct: ct);

        return NoContent();
    }

    private static DelegationDto ToDto(Delegation d) =>
        new(d.Id, d.DelegatorPrincipalId, d.DelegateToUserId, d.StartAt, d.EndAt, d.Active, d.Reason);
}

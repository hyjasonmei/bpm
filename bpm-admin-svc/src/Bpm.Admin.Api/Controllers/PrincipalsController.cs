using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/principals")]
public class PrincipalsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IEffectiveRoleResolver _effectiveRoleResolver;

    public PrincipalsController(AdminDbContext db, IAuditLogger audit, IEffectiveRoleResolver effectiveRoleResolver)
    {
        _db = db;
        _audit = audit;
        _effectiveRoleResolver = effectiveRoleResolver;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PrincipalDto>>> List([FromQuery] PrincipalType? type, CancellationToken ct)
    {
        var q = _db.Principals.AsQueryable();
        if (type.HasValue) q = q.Where(p => p.Type == type.Value);
        var rows = await q.OrderBy(p => p.DisplayName).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PrincipalDto>> GetById(Guid id, CancellationToken ct)
    {
        var p = await _db.Principals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        return Ok(ToDto(p));
    }

    [HttpPost]
    public async Task<ActionResult<PrincipalDto>> Create([FromBody] CreatePrincipalRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
            return BadRequest("DisplayName is required.");

        var p = new Principal
        {
            Type = req.Type,
            DisplayName = req.DisplayName,
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email,
            Active = true,
        };
        _db.Principals.Add(p);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "created",
            targetType: "principal",
            targetId: p.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            after: ToDto(p),
            ct: ct);

        return CreatedAtAction(nameof(GetById), new { id = p.Id }, ToDto(p));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PrincipalDto>> Update(Guid id, [FromBody] UpdatePrincipalRequest req, CancellationToken ct)
    {
        var p = await _db.Principals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        var before = ToDto(p);
        if (!string.IsNullOrWhiteSpace(req.DisplayName)) p.DisplayName = req.DisplayName;
        if (req.Email is not null) p.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email;
        if (req.Active.HasValue) p.Active = req.Active.Value;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "updated",
            targetType: "principal",
            targetId: p.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: ToDto(p),
            ct: ct);

        return Ok(ToDto(p));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var p = await _db.Principals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        var before = ToDto(p);
        p.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "deleted",
            targetType: "principal",
            targetId: p.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            ct: ct);

        return NoContent();
    }

    [HttpGet("{userId:guid}/effective-roles")]
    public async Task<ActionResult<IEnumerable<EffectiveRole>>> GetEffectiveRoles(Guid userId, CancellationToken ct)
    {
        var p = await _db.Principals.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (p is null) return NotFound();
        if (p.Type != PrincipalType.User) return BadRequest("Principal must be a user.");
        var roles = await _effectiveRoleResolver.GetEffectiveRolesAsync(userId, ct);
        return Ok(roles);
    }

    private static PrincipalDto ToDto(Principal p) =>
        new(p.Id, p.Type, p.DisplayName, p.Email, p.Active, p.CreatedAt, p.UpdatedAt);
}

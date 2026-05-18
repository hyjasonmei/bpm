using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/principals/{principalId:guid}/roles")]
public class PrincipalRolesController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public PrincipalRolesController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PrincipalRole>>> List(Guid principalId, CancellationToken ct)
    {
        var rows = await _db.PrincipalRoles.Where(pr => pr.PrincipalId == principalId).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Assign(Guid principalId, [FromBody] AssignRoleRequest req, CancellationToken ct)
    {
        var roleExists = await _db.Roles.AnyAsync(r => r.Id == req.RoleId, ct);
        if (!roleExists) return BadRequest("Role does not exist.");
        var principalExists = await _db.Principals.AnyAsync(p => p.Id == principalId, ct);
        if (!principalExists) return BadRequest("Principal does not exist.");

        var existing = await _db.PrincipalRoles
            .FirstOrDefaultAsync(pr => pr.PrincipalId == principalId && pr.RoleId == req.RoleId, ct);
        if (existing is not null)
        {
            existing.InheritToMembers = req.InheritToMembers;
        }
        else
        {
            _db.PrincipalRoles.Add(new PrincipalRole
            {
                PrincipalId = principalId,
                RoleId = req.RoleId,
                InheritToMembers = req.InheritToMembers,
            });
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: existing is null ? "role_assigned" : "role_updated",
            targetType: "principal_role",
            targetId: $"{principalId}/{req.RoleId}",
            actorUserId: null,
            actorPrincipalId: null,
            after: new { principalId, req.RoleId, req.InheritToMembers },
            ct: ct);

        return NoContent();
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> Revoke(Guid principalId, Guid roleId, CancellationToken ct)
    {
        var row = await _db.PrincipalRoles
            .FirstOrDefaultAsync(pr => pr.PrincipalId == principalId && pr.RoleId == roleId, ct);
        if (row is null) return NotFound();
        _db.PrincipalRoles.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "role_revoked",
            targetType: "principal_role",
            targetId: $"{principalId}/{roleId}",
            actorUserId: null,
            actorPrincipalId: null,
            before: row,
            ct: ct);

        return NoContent();
    }
}

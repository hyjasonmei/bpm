using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/roles")]
public class RolesController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public RolesController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> List(CancellationToken ct)
    {
        var rows = await _db.Roles.OrderBy(r => r.Name).ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] CreateRoleRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");
        if (await _db.Roles.AnyAsync(r => r.Name == req.Name, ct))
            return Conflict("Role with this name already exists.");

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            IsSystem = req.IsSystem,
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "created",
            targetType: "role",
            targetId: role.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            after: ToDto(role),
            ct: ct);

        return Created($"/api/roles/{role.Id}", ToDto(role));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RoleDto>> Update(Guid id, [FromBody] UpdateRoleRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name is required.");

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();
        if (role.IsSystem) return Conflict("System roles cannot be renamed.");

        if (!string.Equals(role.Name, req.Name, StringComparison.Ordinal)
            && await _db.Roles.AnyAsync(r => r.Id != id && r.Name == req.Name, ct))
        {
            return Conflict("Role with this name already exists.");
        }

        var before = ToDto(role);
        role.Name = req.Name;
        role.Description = req.Description;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "updated",
            targetType: "role",
            targetId: role.Id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: ToDto(role),
            ct: ct);

        return Ok(ToDto(role));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();
        if (role.IsSystem) return Conflict("System roles cannot be deleted.");

        var assignedCount = await _db.PrincipalRoles.CountAsync(pr => pr.RoleId == id, ct);
        if (assignedCount > 0)
        {
            return Conflict($"Role is assigned to {assignedCount} principal(s). Revoke assignments first.");
        }

        var before = ToDto(role);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "deleted",
            targetType: "role",
            targetId: id.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            ct: ct);

        return NoContent();
    }

    private static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.IsSystem, r.Description);
}

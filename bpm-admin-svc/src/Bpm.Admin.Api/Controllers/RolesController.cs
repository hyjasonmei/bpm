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

    private static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.IsSystem, r.Description);
}

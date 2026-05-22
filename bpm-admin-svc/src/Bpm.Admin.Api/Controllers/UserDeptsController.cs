using Bpm.Admin.Application.Audit;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record AddUserDeptRequest(Guid DeptId, bool IsPrimary);

[ApiController]
[Route("api/principals/{userId:guid}/dept-memberships")]
public class UserDeptsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public UserDeptsController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDept>>> List(Guid userId, CancellationToken ct)
    {
        var rows = await _db.UserDepts.Where(x => x.UserId == userId).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Add(Guid userId, [FromBody] AddUserDeptRequest req, CancellationToken ct)
    {
        if (await _db.UserDepts.AnyAsync(x => x.UserId == userId && x.DeptId == req.DeptId, ct))
            return Conflict("Membership already exists.");

        // Enforce at most one primary per user
        if (req.IsPrimary)
        {
            var existingPrimary = await _db.UserDepts.Where(x => x.UserId == userId && x.IsPrimary).ToListAsync(ct);
            foreach (var ep in existingPrimary) ep.IsPrimary = false;
        }

        _db.UserDepts.Add(new UserDept { UserId = userId, DeptId = req.DeptId, IsPrimary = req.IsPrimary });
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "user_dept_added",
            targetType: "user_dept",
            targetId: $"{userId}/{req.DeptId}",
            actorUserId: null,
            actorPrincipalId: null,
            after: new { userId, req.DeptId, req.IsPrimary },
            ct: ct);

        return NoContent();
    }

    [HttpDelete("{deptId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, Guid deptId, CancellationToken ct)
    {
        var row = await _db.UserDepts.FirstOrDefaultAsync(x => x.UserId == userId && x.DeptId == deptId, ct);
        if (row is null) return NotFound();
        _db.UserDepts.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "user_dept_removed",
            targetType: "user_dept",
            targetId: $"{userId}/{deptId}",
            actorUserId: null,
            actorPrincipalId: null,
            before: row,
            ct: ct);

        return NoContent();
    }
}

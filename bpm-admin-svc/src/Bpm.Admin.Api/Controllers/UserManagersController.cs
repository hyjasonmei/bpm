using Bpm.Admin.Application.Audit;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record SetManagerRequest(Guid ManagerUserId);
public record UserManagerDto(Guid UserId, Guid? ManagerUserId);

[ApiController]
[Route("api/principals/{userId:guid}/manager")]
public class UserManagersController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public UserManagersController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<UserManagerDto>> Get(Guid userId, CancellationToken ct)
    {
        var row = await _db.UserManagers
            .Where(m => m.UserId == userId)
            .Select(m => new UserManagerDto(m.UserId, m.ManagerUserId))
            .FirstOrDefaultAsync(ct);
        return row ?? new UserManagerDto(userId, null);
    }

    [HttpPut]
    public async Task<IActionResult> Set(Guid userId, [FromBody] SetManagerRequest req, CancellationToken ct)
    {
        if (req.ManagerUserId == userId)
            return BadRequest("A user cannot be their own manager.");
        if (!await _db.Principals.AnyAsync(p => p.Id == userId && p.Type == PrincipalType.User && p.DeletedAt == null, ct))
            return NotFound();
        if (!await _db.Principals.AnyAsync(p => p.Id == req.ManagerUserId && p.Type == PrincipalType.User && p.DeletedAt == null, ct))
            return BadRequest("Manager user not found.");

        // Check for cycle: walk up from the proposed manager and ensure userId
        // is not encountered (a reporting loop would hang manager-chain approvals).
        var current = req.ManagerUserId;
        var visited = new HashSet<Guid>();
        while (true)
        {
            if (!visited.Add(current)) break; // cycle in existing graph (shouldn't happen)
            if (current == userId) return BadRequest("Setting this manager would create a reporting cycle.");
            var next = await _db.UserManagers
                .Where(m => m.UserId == current)
                .Select(m => (Guid?)m.ManagerUserId)
                .FirstOrDefaultAsync(ct);
            if (!next.HasValue) break;
            current = next.Value;
        }

        var row = await _db.UserManagers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        var before = row is null ? null : new { row.UserId, row.ManagerUserId };
        if (row is null)
        {
            _db.UserManagers.Add(new UserManager { UserId = userId, ManagerUserId = req.ManagerUserId, AssignedAt = DateTime.UtcNow });
        }
        else
        {
            row.ManagerUserId = req.ManagerUserId;
            row.AssignedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "user_manager_set",
            targetType: "user_manager",
            targetId: userId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: new { userId, req.ManagerUserId },
            ct: ct);

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct)
    {
        var row = await _db.UserManagers.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) return NotFound();
        _db.UserManagers.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "user_manager_removed",
            targetType: "user_manager",
            targetId: userId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: row,
            ct: ct);

        return NoContent();
    }
}

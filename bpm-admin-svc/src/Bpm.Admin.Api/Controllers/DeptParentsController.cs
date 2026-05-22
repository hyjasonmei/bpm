using Bpm.Admin.Application.Audit;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record SetDeptParentRequest(Guid? ParentDeptId);
public record DeptParentDto(Guid DeptId, Guid? ParentDeptId);

[ApiController]
[Route("api/principals/{deptId:guid}/parent")]
public class DeptParentsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public DeptParentsController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<DeptParentDto>> Get(Guid deptId, CancellationToken ct)
    {
        var row = await _db.DeptParents
            .Where(dp => dp.DeptId == deptId)
            .Select(dp => new DeptParentDto(dp.DeptId, dp.ParentDeptId))
            .FirstOrDefaultAsync(ct);
        return row ?? new DeptParentDto(deptId, null);
    }

    [HttpPut]
    public async Task<IActionResult> Set(Guid deptId, [FromBody] SetDeptParentRequest req, CancellationToken ct)
    {
        if (req.ParentDeptId == deptId)
            return BadRequest("A dept cannot be its own parent.");

        // Check for cycle: walk up from parent and ensure deptId not encountered
        if (req.ParentDeptId.HasValue)
        {
            var current = req.ParentDeptId.Value;
            var visited = new HashSet<Guid>();
            while (true)
            {
                if (!visited.Add(current)) break; // cycle in existing graph (shouldn't happen)
                if (current == deptId) return BadRequest("Setting this parent would create a dept cycle.");
                var next = await _db.DeptParents
                    .Where(dp => dp.DeptId == current)
                    .Select(dp => dp.ParentDeptId)
                    .FirstOrDefaultAsync(ct);
                if (!next.HasValue) break;
                current = next.Value;
            }
        }

        var row = await _db.DeptParents.FirstOrDefaultAsync(x => x.DeptId == deptId, ct);
        var before = row is null ? null : new { row.DeptId, row.ParentDeptId };
        if (row is null)
        {
            row = new DeptParent { DeptId = deptId, ParentDeptId = req.ParentDeptId };
            _db.DeptParents.Add(row);
        }
        else
        {
            row.ParentDeptId = req.ParentDeptId;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "dept_parent_set",
            targetType: "dept_parent",
            targetId: deptId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: new { row.DeptId, row.ParentDeptId },
            ct: ct);

        return NoContent();
    }
}

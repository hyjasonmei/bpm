using Bpm.Admin.Application.Audit;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record SetDeptHeadRequest(Guid HeadUserId);
public record DeptHeadDto(Guid DeptId, Guid? HeadUserId);

[ApiController]
[Route("api/principals/{deptId:guid}/head")]
public class DeptHeadsController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;

    public DeptHeadsController(AdminDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<DeptHeadDto>> Get(Guid deptId, CancellationToken ct)
    {
        var row = await _db.DeptHeads
            .Where(h => h.DeptId == deptId)
            .Select(h => new DeptHeadDto(h.DeptId, h.HeadUserId))
            .FirstOrDefaultAsync(ct);
        return row ?? new DeptHeadDto(deptId, null);
    }

    [HttpPut]
    public async Task<IActionResult> Set(Guid deptId, [FromBody] SetDeptHeadRequest req, CancellationToken ct)
    {
        if (!await _db.Principals.AnyAsync(p => p.Id == deptId && p.Type == PrincipalType.Dept && p.DeletedAt == null, ct))
            return NotFound();
        if (!await _db.Principals.AnyAsync(p => p.Id == req.HeadUserId && p.Type == PrincipalType.User && p.DeletedAt == null, ct))
            return BadRequest("Head user not found.");

        var row = await _db.DeptHeads.FirstOrDefaultAsync(x => x.DeptId == deptId, ct);
        var before = row is null ? null : new { row.DeptId, row.HeadUserId };
        if (row is null)
        {
            _db.DeptHeads.Add(new DeptHead { DeptId = deptId, HeadUserId = req.HeadUserId, AssignedAt = DateTime.UtcNow });
        }
        else
        {
            row.HeadUserId = req.HeadUserId;
            row.AssignedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "dept_head_set",
            targetType: "dept_head",
            targetId: deptId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: before,
            after: new { deptId, req.HeadUserId },
            ct: ct);

        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Remove(Guid deptId, CancellationToken ct)
    {
        var row = await _db.DeptHeads.FirstOrDefaultAsync(x => x.DeptId == deptId, ct);
        if (row is null) return NotFound();
        _db.DeptHeads.Remove(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "dept_head_removed",
            targetType: "dept_head",
            targetId: deptId.ToString(),
            actorUserId: null,
            actorPrincipalId: null,
            before: row,
            ct: ct);

        return NoContent();
    }
}

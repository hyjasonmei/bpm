using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Controllers;

public record AddGroupMemberRequest(Guid MemberPrincipalId, PrincipalType MemberType);

[ApiController]
[Route("api/principals/{groupId:guid}/members")]
public class GroupMembersController : ControllerBase
{
    private readonly AdminDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IGroupMembershipService _membership;

    public GroupMembersController(AdminDbContext db, IAuditLogger audit, IGroupMembershipService membership)
    {
        _db = db;
        _audit = audit;
        _membership = membership;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GroupMember>>> List(Guid groupId, CancellationToken ct)
    {
        var rows = await _db.GroupMembers.Where(x => x.GroupId == groupId).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Add(Guid groupId, [FromBody] AddGroupMemberRequest req, CancellationToken ct)
    {
        try
        {
            await _membership.AddMemberAsync(groupId, req.MemberPrincipalId, req.MemberType, ct);
        }
        catch (GroupCycleException ex)
        {
            return BadRequest(ex.Message);
        }

        await _audit.LogAsync(
            actionType: "group_member_added",
            targetType: "group_member",
            targetId: $"{groupId}/{req.MemberPrincipalId}",
            actorUserId: null,
            actorPrincipalId: null,
            after: new { groupId, req.MemberPrincipalId, req.MemberType },
            ct: ct);

        return NoContent();
    }

    [HttpDelete("{memberId:guid}")]
    public async Task<IActionResult> Remove(Guid groupId, Guid memberId, CancellationToken ct)
    {
        var row = await _db.GroupMembers
            .FirstOrDefaultAsync(x => x.GroupId == groupId && x.MemberPrincipalId == memberId, ct);
        if (row is null) return NotFound();

        await _membership.RemoveMemberAsync(groupId, memberId, ct);

        await _audit.LogAsync(
            actionType: "group_member_removed",
            targetType: "group_member",
            targetId: $"{groupId}/{memberId}",
            actorUserId: null,
            actorPrincipalId: null,
            before: row,
            ct: ct);

        return NoContent();
    }
}

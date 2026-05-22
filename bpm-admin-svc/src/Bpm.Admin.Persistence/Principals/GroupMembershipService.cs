using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Principals;

public class GroupMembershipService : IGroupMembershipService
{
    private readonly AdminDbContext _db;

    public GroupMembershipService(AdminDbContext db)
    {
        _db = db;
    }

    public async Task AddMemberAsync(Guid groupId, Guid memberPrincipalId, PrincipalType memberType, CancellationToken ct = default)
    {
        if (groupId == memberPrincipalId)
        {
            throw new GroupCycleException(groupId, memberPrincipalId);
        }

        if (memberType == PrincipalType.Group)
        {
            if (await WouldCreateCycleAsync(groupId, memberPrincipalId, ct))
            {
                throw new GroupCycleException(groupId, memberPrincipalId);
            }
        }

        _db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            MemberPrincipalId = memberPrincipalId,
            MemberType = memberType,
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid memberPrincipalId, CancellationToken ct = default)
    {
        var existing = await _db.GroupMembers
            .Where(m => m.GroupId == groupId && m.MemberPrincipalId == memberPrincipalId)
            .FirstOrDefaultAsync(ct);
        if (existing is null) return;
        _db.GroupMembers.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns true if adding <paramref name="candidateMember"/> as a member of <paramref name="target"/>
    /// would create a cycle. Walks the membership graph from candidateMember (as a group) downward
    /// to see if target appears.
    /// </summary>
    private async Task<bool> WouldCreateCycleAsync(Guid target, Guid candidateMember, CancellationToken ct)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(candidateMember);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (current == target) return true;

            var children = await _db.GroupMembers
                .Where(m => m.GroupId == current && m.MemberType == PrincipalType.Group)
                .Select(m => m.MemberPrincipalId)
                .ToListAsync(ct);

            foreach (var child in children)
            {
                stack.Push(child);
            }
        }

        return false;
    }
}

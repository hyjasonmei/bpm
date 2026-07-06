using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Roles;

/// <summary>
/// Resolves a user's effective roles by walking three layers:
///   1. Direct PrincipalRole assignments where principal = user
///   2. Dept tree (UserDept → DeptParent ↑) — include role only if inherit_to_members
///   3. Group graph (GroupMember walked transitively) — include role only if inherit_to_members
///
/// Returns one EffectiveRole row per (roleId, sourcePrincipalId).
/// Query-time computation; later swap to a materialized view if perf demands.
/// </summary>
public class EffectiveRoleResolver : IEffectiveRoleResolver
{
    private readonly AdminDbContext _db;

    public EffectiveRoleResolver(AdminDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<EffectiveRole>> GetEffectiveRolesAsync(Guid userId, CancellationToken ct = default)
    {
        var result = new HashSet<EffectiveRole>();

        // Layer 1: direct assignments
        var direct = await _db.PrincipalRoles
            .Where(pr => pr.PrincipalId == userId)
            .Select(pr => new { pr.RoleId, pr.PrincipalId })
            .ToListAsync(ct);
        foreach (var r in direct)
        {
            result.Add(new EffectiveRole(r.RoleId, r.PrincipalId, ViaInherit: false));
        }

        // Layer 2: depts. A role on one of the user's OWN depts inherits with
        // just InheritToMembers; a role on an ANCESTOR dept reaches this user
        // only when the assignment also opted into IncludeSubDepts. Same rule
        // as bpm-svc's routing (PrincipalDirectory) — keep the two in lockstep
        // or the admin UI shows people the launcher can't route to.
        var directDeptIds = await _db.UserDepts
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DeptId)
            .ToListAsync(ct);
        var allDeptIds = await WalkDeptAncestorsAsync(directDeptIds, ct);
        if (allDeptIds.Count > 0)
        {
            var deptRoles = await _db.PrincipalRoles
                .Where(pr => allDeptIds.Contains(pr.PrincipalId) && pr.InheritToMembers)
                .Select(pr => new { pr.RoleId, pr.PrincipalId, pr.IncludeSubDepts })
                .ToListAsync(ct);
            foreach (var r in deptRoles)
            {
                if (!directDeptIds.Contains(r.PrincipalId) && !r.IncludeSubDepts) continue;
                result.Add(new EffectiveRole(r.RoleId, r.PrincipalId, ViaInherit: true));
            }
        }

        // Layer 3: groups (transitive)
        var groupIds = await WalkGroupAncestorsAsync(userId, ct);
        if (groupIds.Count > 0)
        {
            var groupRoles = await _db.PrincipalRoles
                .Where(pr => groupIds.Contains(pr.PrincipalId) && pr.InheritToMembers)
                .Select(pr => new { pr.RoleId, pr.PrincipalId })
                .ToListAsync(ct);
            foreach (var r in groupRoles)
            {
                result.Add(new EffectiveRole(r.RoleId, r.PrincipalId, ViaInherit: true));
            }
        }

        return result;
    }

    private async Task<HashSet<Guid>> WalkDeptAncestorsAsync(IReadOnlyCollection<Guid> immediateDepts, CancellationToken ct)
    {
        var all = new HashSet<Guid>(immediateDepts);
        var frontier = new Queue<Guid>(immediateDepts);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var parent = await _db.DeptParents
                .Where(dp => dp.DeptId == current)
                .Select(dp => dp.ParentDeptId)
                .FirstOrDefaultAsync(ct);
            if (parent.HasValue && all.Add(parent.Value))
            {
                frontier.Enqueue(parent.Value);
            }
        }

        return all;
    }

    /// <summary>
    /// Finds every group that contains the user, walking up the group membership graph.
    /// "Up" means: if group G1 contains group G2 and G2 contains the user, both G1 and G2 are ancestors.
    /// </summary>
    private async Task<HashSet<Guid>> WalkGroupAncestorsAsync(Guid userId, CancellationToken ct)
    {
        var visited = new HashSet<Guid>();
        var seedGroups = await _db.GroupMembers
            .Where(gm => gm.MemberPrincipalId == userId && gm.MemberType == PrincipalType.User)
            .Select(gm => gm.GroupId)
            .ToListAsync(ct);

        var frontier = new Queue<Guid>(seedGroups);
        foreach (var g in seedGroups) visited.Add(g);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            var parents = await _db.GroupMembers
                .Where(gm => gm.MemberPrincipalId == current && gm.MemberType == PrincipalType.Group)
                .Select(gm => gm.GroupId)
                .ToListAsync(ct);
            foreach (var p in parents)
            {
                if (visited.Add(p)) frontier.Enqueue(p);
            }
        }

        return visited;
    }
}

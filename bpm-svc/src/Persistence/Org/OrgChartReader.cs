using Bpm.Application.Org;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Org;

/// Reads against the unified Admin_* identity tables (SharedIdentity)
/// instead of the retired bpm-local Principals / Users / Departments
/// tables.
public sealed class OrgChartReader(AppDbContext db) : IOrgChartReader
{
    public async Task<Guid?> GetUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var exists = await db.SharedPrincipals.AsNoTracking()
            .AnyAsync(p => p.Id == userId
                        && p.Type == SharedPrincipalType.User
                        && p.DeletedAt == null, ct);
        return exists ? userId : null;
    }

    public async Task<Guid?> GetManagerIdAsync(Guid userId, CancellationToken ct = default)
    {
        var managerId = await db.SharedUserManagers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => (Guid?)m.ManagerUserId)
            .FirstOrDefaultAsync(ct);
        return managerId;
    }

    public async Task<Guid?> GetPrimaryDepartmentIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.SharedUserDepts.AsNoTracking()
            .Where(ud => ud.UserId == userId && ud.IsPrimary)
            .Select(ud => (Guid?)ud.DeptId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> GetDepartmentIdAsync(Guid deptId, CancellationToken ct = default)
    {
        var exists = await db.SharedPrincipals.AsNoTracking()
            .AnyAsync(p => p.Id == deptId
                        && p.Type == SharedPrincipalType.Dept
                        && p.DeletedAt == null, ct);
        return exists ? deptId : null;
    }

    public async Task<Guid?> GetDepartmentParentIdAsync(Guid deptId, CancellationToken ct = default)
    {
        return await db.SharedDeptParents.AsNoTracking()
            .Where(dp => dp.DeptId == deptId)
            .Select(dp => dp.ParentDeptId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> GetDepartmentHeadIdAsync(Guid deptId, CancellationToken ct = default)
    {
        return await db.SharedDeptHeads.AsNoTracking()
            .Where(dh => dh.DeptId == deptId)
            .Select(dh => (Guid?)dh.HeadUserId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<GroupExpansion> ExpandGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        // BFS over group → members. Members may themselves be Groups, in
        // which case we enqueue them. Departments expand to all primary-
        // dept members. Cycle detection via visitedGroups; if we meet a
        // previously-visited group we abort and surface the cycle path.
        var users = new HashSet<Guid>();
        var visitedGroups = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        var pathStack = new List<Guid>();
        queue.Enqueue(groupId);

        while (queue.Count > 0)
        {
            var gid = queue.Dequeue();
            if (!visitedGroups.Add(gid))
            {
                pathStack.Add(gid);
                return new GroupExpansion(users, CyclePath: pathStack);
            }
            pathStack.Add(gid);

            var members = await db.SharedGroupMembers.AsNoTracking()
                .Where(m => m.GroupId == gid)
                .ToListAsync(ct);

            foreach (var m in members)
            {
                if (m.MemberType == SharedPrincipalType.User)
                {
                    users.Add(m.MemberPrincipalId);
                }
                else if (m.MemberType == SharedPrincipalType.Group)
                {
                    queue.Enqueue(m.MemberPrincipalId);
                }
                else if (m.MemberType == SharedPrincipalType.Dept)
                {
                    var deptUsers = await db.SharedUserDepts.AsNoTracking()
                        .Where(ud => ud.DeptId == m.MemberPrincipalId)
                        .Select(ud => ud.UserId)
                        .ToListAsync(ct);
                    foreach (var uid in deptUsers) users.Add(uid);
                }
            }
        }

        return new GroupExpansion(users);
    }

    public async Task<IReadOnlyList<(Guid PrincipalId, Guid RoleId)>> GetRoleAssigneesAsync(
        string roleCode, string? flowCode = null, CancellationToken ct = default)
    {
        // Roles are keyed by the stable Code (SCREAMING_SNAKE); chef-shipped
        // flows resolve role:<code>. flowCode scoping is currently unused.
        _ = flowCode;
        var q = from pr in db.SharedPrincipalRoles.AsNoTracking()
                join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
                where r.Code == roleCode
                select new { pr.PrincipalId, pr.RoleId };
        var rows = await q.ToListAsync(ct);
        return rows.Select(x => (x.PrincipalId, x.RoleId)).ToList();
    }
}

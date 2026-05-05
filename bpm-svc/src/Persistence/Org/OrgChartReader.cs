using Bpm.Application.Org;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Org;

public sealed class OrgChartReader(AppDbContext db) : IOrgChartReader
{
    public Task<User?> GetUserAsync(Guid userId, CancellationToken ct = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<User?> GetManagerAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u?.ManagerId is null) return null;
        return await db.Users.FirstOrDefaultAsync(x => x.Id == u.ManagerId, ct);
    }

    public async Task<Department?> GetDepartmentOfAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u?.DepartmentId is null) return null;
        return await db.Departments.FirstOrDefaultAsync(d => d.Id == u.DepartmentId, ct);
    }

    public Task<Department?> GetDepartmentAsync(Guid deptId, CancellationToken ct = default)
        => db.Departments.FirstOrDefaultAsync(d => d.Id == deptId, ct);

    public async Task<Department?> GetDepartmentParentAsync(Guid deptId, CancellationToken ct = default)
    {
        var d = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deptId, ct);
        if (d?.ParentId is null) return null;
        return await db.Departments.FirstOrDefaultAsync(x => x.Id == d.ParentId, ct);
    }

    public async Task<User?> GetDepartmentHeadAsync(Guid deptId, CancellationToken ct = default)
    {
        var d = await db.Departments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == deptId, ct);
        if (d?.HeadUserId is null) return null;
        return await db.Users.FirstOrDefaultAsync(u => u.Id == d.HeadUserId, ct);
    }

    public async Task<GroupExpansion> ExpandGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        // BFS over group→members. Members may themselves be Groups, in which
        // case we enqueue them and continue. Cycle detection via visited set
        // of group ids; if we meet a previously-visited group we abort and
        // surface the cycle path so the caller can audit.
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

            var members = await db.GroupMembers.AsNoTracking()
                .Where(m => m.GroupId == gid)
                .Select(m => m.PrincipalId)
                .ToListAsync(ct);

            foreach (var pid in members)
            {
                // Determine principal type by probing the typed tables.
                if (await db.Users.AnyAsync(u => u.Id == pid, ct))
                {
                    users.Add(pid);
                }
                else if (await db.Groups.AnyAsync(g => g.Id == pid, ct))
                {
                    queue.Enqueue(pid);
                }
                else if (await db.Departments.AnyAsync(d => d.Id == pid, ct))
                {
                    // Department member of group → expand to all users in the dept
                    var deptUsers = await db.Users.AsNoTracking()
                        .Where(u => u.DepartmentId == pid)
                        .Select(u => u.Id)
                        .ToListAsync(ct);
                    foreach (var uid in deptUsers) users.Add(uid);
                }
                // Unknown principal type → silently skip (FK should prevent this)
            }
        }

        return new GroupExpansion(users);
    }

    public async Task<IReadOnlyList<(Guid PrincipalId, Guid RoleId)>> GetRoleAssigneesAsync(
        string roleCode, string? flowCode = null, CancellationToken ct = default)
    {
        var q = from ra in db.RoleAssignments.AsNoTracking()
                join r in db.Roles.AsNoTracking() on ra.RoleId equals r.Id
                where r.Code == roleCode
                   && (r.Scope == RoleScope.System
                       || (r.Scope == RoleScope.Flow && r.FlowCode == flowCode))
                select new { ra.PrincipalId, ra.RoleId };
        var rows = await q.ToListAsync(ct);
        return rows.Select(x => (x.PrincipalId, x.RoleId)).ToList();
    }
}

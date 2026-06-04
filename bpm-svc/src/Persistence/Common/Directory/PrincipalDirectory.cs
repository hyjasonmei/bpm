using Bpm.Application.Common.Directory;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Common.Directory;

/// <summary>
/// EF-backed implementation of <see cref="IPrincipalDirectory"/>. Reads
/// against SharedIdentity (the read-only mirror of admin's
/// <c>Admin_Principals</c> / <c>Admin_Roles</c> / <c>Admin_PrincipalRoles</c>
/// tables).
/// </summary>
public sealed class PrincipalDirectory(AppDbContext db) : IPrincipalDirectory
{
    public async Task<PrincipalInfo?> GetByIdAsync(Guid principalId, CancellationToken ct = default)
    {
        var p = await db.SharedPrincipals.AsNoTracking()
            .Where(x => x.Id == principalId && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Type, x.DisplayName, x.Email, x.Active })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;
        return new PrincipalInfo(p.Id, MapKind(p.Type), p.DisplayName, p.Email, p.Active);
    }

    public async Task<IReadOnlyDictionary<Guid, PrincipalInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> principalIds, CancellationToken ct = default)
    {
        if (principalIds.Count == 0) return new Dictionary<Guid, PrincipalInfo>();
        var ids = principalIds.Distinct().ToArray();
        var rows = await db.SharedPrincipals.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Type, x.DisplayName, x.Email, x.Active })
            .ToListAsync(ct);
        return rows.ToDictionary(
            r => r.Id,
            r => new PrincipalInfo(r.Id, MapKind(r.Type), r.DisplayName, r.Email, r.Active));
    }

    public async Task<Guid?> FindFirstUserInRoleAsync(string roleName, CancellationToken ct = default)
    {
        // roleName is the stable role Code (SCREAMING_SNAKE), not the display Name.
        var roleId = await db.SharedRoles.AsNoTracking()
            .Where(r => r.Code == roleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is null) return null;

        // A role may be granted to a User directly, or to a Dept / Group whose
        // members inherit it (InheritToMembers). Collect every candidate user
        // id, then pick the first active one. This mirrors the effective-role
        // expansion documented on SharedPrincipalRole.
        var grants = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join p in db.SharedPrincipals.AsNoTracking() on pr.PrincipalId equals p.Id
            where pr.RoleId == roleId && p.DeletedAt == null
            select new { p.Id, p.Type, pr.InheritToMembers }).ToListAsync(ct);
        if (grants.Count == 0) return null;

        var candidates = new HashSet<Guid>();
        foreach (var g in grants.Where(g => g.Type == SharedPrincipalType.User))
            candidates.Add(g.Id);

        var deptIds = grants
            .Where(g => g.Type == SharedPrincipalType.Dept && g.InheritToMembers)
            .Select(g => g.Id).ToList();
        if (deptIds.Count > 0)
        {
            var deptUsers = await db.SharedUserDepts.AsNoTracking()
                .Where(ud => deptIds.Contains(ud.DeptId))
                .Select(ud => ud.UserId).ToListAsync(ct);
            foreach (var u in deptUsers) candidates.Add(u);
        }

        var groupIds = grants
            .Where(g => g.Type == SharedPrincipalType.Group && g.InheritToMembers)
            .Select(g => g.Id).ToList();
        foreach (var u in await ExpandGroupsToUsersAsync(groupIds, ct))
            candidates.Add(u);

        if (candidates.Count == 0) return null;

        var userId = await db.SharedPrincipals.AsNoTracking()
            .Where(p => candidates.Contains(p.Id)
                        && p.Type == SharedPrincipalType.User
                        && p.Active
                        && p.DeletedAt == null)
            .OrderBy(p => p.DisplayName)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        return userId;
    }

    /// <summary>
    /// Flattens groups to their member user ids, following nested-group
    /// membership transitively. Cycle-safe via a visited set.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> ExpandGroupsToUsersAsync(
        IReadOnlyCollection<Guid> groupIds, CancellationToken ct)
    {
        if (groupIds.Count == 0) return Array.Empty<Guid>();
        var users = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>(groupIds);
        while (queue.Count > 0)
        {
            var gid = queue.Dequeue();
            if (!visited.Add(gid)) continue;
            var members = await db.SharedGroupMembers.AsNoTracking()
                .Where(m => m.GroupId == gid)
                .Select(m => new { m.MemberPrincipalId, m.MemberType })
                .ToListAsync(ct);
            foreach (var m in members)
            {
                if (m.MemberType == SharedPrincipalType.User) users.Add(m.MemberPrincipalId);
                else if (m.MemberType == SharedPrincipalType.Group) queue.Enqueue(m.MemberPrincipalId);
            }
        }
        return users;
    }

    private static PrincipalKind MapKind(SharedPrincipalType t) => t switch
    {
        SharedPrincipalType.User  => PrincipalKind.User,
        SharedPrincipalType.Dept  => PrincipalKind.Department,
        SharedPrincipalType.Group => PrincipalKind.Group,
        _ => PrincipalKind.Other,
    };
}

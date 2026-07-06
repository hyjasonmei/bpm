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
        => (await GetUsersInRoleAsync(roleName, ct)).Select(x => (Guid?)x).FirstOrDefault();

    public async Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string roleName, CancellationToken ct = default)
    {
        // roleName is the stable role Code (SCREAMING_SNAKE), not the display Name.
        var roleId = await db.SharedRoles.AsNoTracking()
            .Where(r => r.Code == roleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is null) return Array.Empty<Guid>();

        // A role may be granted to a User directly, or to a Dept / Group whose
        // members inherit it (InheritToMembers). Collect every candidate user id.
        var grants = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join p in db.SharedPrincipals.AsNoTracking() on pr.PrincipalId equals p.Id
            where pr.RoleId == roleId && p.DeletedAt == null
            select new { p.Id, p.Type, pr.InheritToMembers, pr.IncludeSubDepts }).ToListAsync(ct);
        if (grants.Count == 0) return Array.Empty<Guid>();

        var candidates = new HashSet<Guid>();
        foreach (var g in grants.Where(g => g.Type == SharedPrincipalType.User))
            candidates.Add(g.Id);

        // Dept grants reach the dept's direct members; with IncludeSubDepts the
        // grant additionally covers every DESCENDANT dept's members. Same flag
        // semantics as admin's EffectiveRoleResolver — keep in lockstep.
        var deptIds = grants
            .Where(g => g.Type == SharedPrincipalType.Dept && g.InheritToMembers)
            .Select(g => g.Id).ToHashSet();
        var subtreeRoots = grants
            .Where(g => g.Type == SharedPrincipalType.Dept && g.InheritToMembers && g.IncludeSubDepts)
            .Select(g => g.Id).ToList();
        foreach (var d in await ExpandDeptDescendantsAsync(subtreeRoots, ct))
            deptIds.Add(d);
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

        if (candidates.Count == 0) return Array.Empty<Guid>();

        // Active Users only, ordered by display name (stable "first" for FindFirst).
        return await db.SharedPrincipals.AsNoTracking()
            .Where(p => candidates.Contains(p.Id)
                        && p.Type == SharedPrincipalType.User
                        && p.Active
                        && p.DeletedAt == null)
            .OrderBy(p => p.DisplayName)
            .Select(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default)
    {
        // Grants covering this user: their own id (direct), their depts, and every
        // group they belong to (transitively). Dept/group grants count only when
        // InheritToMembers; direct user grants always count. A grant on an
        // ANCESTOR dept counts only when it opted into IncludeSubDepts — same
        // rule as GetUsersInRoleAsync and admin's EffectiveRoleResolver.
        var deptIds = await db.SharedUserDepts.AsNoTracking()
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DeptId).ToListAsync(ct);
        var ancestorDeptIds = (await WalkDeptAncestorsAsync(deptIds, ct))
            .Where(id => !deptIds.Contains(id)).ToList();

        var groupIds = await GroupsForUserAsync(userId, ct);

        var codes = new HashSet<string>(StringComparer.Ordinal);

        // direct grants (always count)
        var direct = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            where pr.PrincipalId == userId
            select r.Code).ToListAsync(ct);
        foreach (var c in direct) codes.Add(c);

        // inherited grants via dept / group (only when InheritToMembers)
        var inheritIds = deptIds.Concat(groupIds).Distinct().ToList();
        if (inheritIds.Count > 0)
        {
            var inherited = await (
                from pr in db.SharedPrincipalRoles.AsNoTracking()
                join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
                where inheritIds.Contains(pr.PrincipalId) && pr.InheritToMembers
                select r.Code).ToListAsync(ct);
            foreach (var c in inherited) codes.Add(c);
        }

        // ancestor-dept grants: only those that opted into IncludeSubDepts
        if (ancestorDeptIds.Count > 0)
        {
            var subtree = await (
                from pr in db.SharedPrincipalRoles.AsNoTracking()
                join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
                where ancestorDeptIds.Contains(pr.PrincipalId) && pr.InheritToMembers && pr.IncludeSubDepts
                select r.Code).ToListAsync(ct);
            foreach (var c in subtree) codes.Add(c);
        }

        return codes;
    }

    /// <summary>Every descendant dept of the given roots (children, grand-
    /// children, …) via DeptParents. Cycle-safe; roots not included.</summary>
    private async Task<IReadOnlyCollection<Guid>> ExpandDeptDescendantsAsync(
        IReadOnlyCollection<Guid> rootDeptIds, CancellationToken ct)
    {
        if (rootDeptIds.Count == 0) return Array.Empty<Guid>();
        var result = new HashSet<Guid>();
        var visited = new HashSet<Guid>(rootDeptIds);
        var queue = new Queue<Guid>(rootDeptIds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await db.SharedDeptParents.AsNoTracking()
                .Where(dp => dp.ParentDeptId == current)
                .Select(dp => dp.DeptId).ToListAsync(ct);
            foreach (var c in children)
            {
                if (visited.Add(c)) { result.Add(c); queue.Enqueue(c); }
            }
        }
        return result;
    }

    /// <summary>The given depts plus every ancestor (parent, grand-parent, …)
    /// via DeptParents. Cycle-safe.</summary>
    private async Task<IReadOnlyCollection<Guid>> WalkDeptAncestorsAsync(
        IReadOnlyCollection<Guid> deptIds, CancellationToken ct)
    {
        var all = new HashSet<Guid>(deptIds);
        var queue = new Queue<Guid>(deptIds);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var parent = await db.SharedDeptParents.AsNoTracking()
                .Where(dp => dp.DeptId == current)
                .Select(dp => dp.ParentDeptId)
                .FirstOrDefaultAsync(ct);
            if (parent.HasValue && all.Add(parent.Value)) queue.Enqueue(parent.Value);
        }
        return all;
    }

    /// <summary>
    /// Groups the user belongs to, following nested-group membership UPWARD
    /// (a group that contains a group the user is in). Cycle-safe.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> GroupsForUserAsync(Guid userId, CancellationToken ct)
    {
        var result = new HashSet<Guid>();
        var seed = await db.SharedGroupMembers.AsNoTracking()
            .Where(m => m.MemberPrincipalId == userId && m.MemberType == SharedPrincipalType.User)
            .Select(m => m.GroupId).ToListAsync(ct);
        var queue = new Queue<Guid>(seed);
        while (queue.Count > 0)
        {
            var gid = queue.Dequeue();
            if (!result.Add(gid)) continue;
            var parents = await db.SharedGroupMembers.AsNoTracking()
                .Where(m => m.MemberPrincipalId == gid && m.MemberType == SharedPrincipalType.Group)
                .Select(m => m.GroupId).ToListAsync(ct);
            foreach (var p in parents) queue.Enqueue(p);
        }
        return result;
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

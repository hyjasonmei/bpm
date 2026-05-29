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
        var roleId = await db.SharedRoles.AsNoTracking()
            .Where(r => r.Name == roleName)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is null) return null;

        var userId = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join p in db.SharedPrincipals.AsNoTracking() on pr.PrincipalId equals p.Id
            where pr.RoleId == roleId
                  && p.Type == SharedPrincipalType.User
                  && p.Active
                  && p.DeletedAt == null
            orderby p.DisplayName
            select (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return userId;
    }

    private static PrincipalKind MapKind(SharedPrincipalType t) => t switch
    {
        SharedPrincipalType.User  => PrincipalKind.User,
        SharedPrincipalType.Dept  => PrincipalKind.Department,
        SharedPrincipalType.Group => PrincipalKind.Group,
        _ => PrincipalKind.Other,
    };
}

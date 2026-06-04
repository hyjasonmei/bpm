using Bpm.Application.Admin;
using Bpm.Application.Admin.Dtos;
using Bpm.Application.Common.Exceptions;
using Bpm.Domain.Entities.Authz;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Admin;

/// <summary>
/// Thin admin tool that reads + writes the unified Admin_* identity
/// tables via SharedX. Audit lives in bpm-svc's local
/// RoleAssignmentChanges table (runtime-side, not duplicated in admin).
/// </summary>
public sealed class RoleAdminService(AppDbContext db) : IRoleAdminService
{
    // Admin role name in the unified seed (admin-svc Seeder). Renamed from
    // bpm-local "admin" → admin-svc's "SystemAdmin" by unify-user-store.
    private const string AdminRoleName = "SYSTEM_ADMIN";

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var rows = await (
            from r in db.SharedRoles.AsNoTracking()
            select new RoleSummaryDto(
                r.Id, r.Name, r.IsSystem,
                db.SharedPrincipalRoles.Count(pr => pr.RoleId == r.Id))
        ).ToListAsync(ct);
        return rows;
    }

    public async Task<PagedResult<UserSummaryDto>> ListUsersAsync(string? q, int page, int pageSize, string? roleName, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var query = db.SharedPrincipals.AsNoTracking()
            .Where(p => p.Type == SharedPrincipalType.User && p.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(u =>
                EF.Functions.Like(u.DisplayName, pattern)
                || (u.Email != null && EF.Functions.Like(u.Email, pattern)));
        }
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            var name = roleName.Trim();
            query = query.Where(u => db.SharedPrincipalRoles.Any(pr =>
                pr.PrincipalId == u.Id
                && db.SharedRoles.Any(r => r.Id == pr.RoleId && r.Code == name)));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UserSummaryDto>(users.Count);
        foreach (var u in users)
        {
            var roleCount = await db.SharedPrincipalRoles.CountAsync(pr => pr.PrincipalId == u.Id, ct);
            var deptCode = await (
                from ud in db.SharedUserDepts.AsNoTracking()
                where ud.UserId == u.Id && ud.IsPrimary
                join d in db.SharedPrincipals.AsNoTracking() on ud.DeptId equals d.Id
                select (string?)d.DisplayName).FirstOrDefaultAsync(ct);
            items.Add(new UserSummaryDto(u.Id, u.DisplayName, u.Email ?? string.Empty, deptCode, u.Active, roleCount));
        }
        return new PagedResult<UserSummaryDto>(items, page, pageSize, total);
    }

    public async Task<UserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.Type == SharedPrincipalType.User, ct)
            ?? throw new NotFoundException("User", userId);

        var deptCode = await (
            from ud in db.SharedUserDepts.AsNoTracking()
            where ud.UserId == user.Id && ud.IsPrimary
            join d in db.SharedPrincipals.AsNoTracking() on ud.DeptId equals d.Id
            select (string?)d.DisplayName).FirstOrDefaultAsync(ct);

        var assignments = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            where pr.PrincipalId == userId
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            select new { pr, r }).ToListAsync(ct);

        var dtos = new List<AssignmentDto>(assignments.Count);
        foreach (var x in assignments)
        {
            var lastAssign = await db.RoleAssignmentChanges.AsNoTracking()
                .Where(c => c.TargetUserId == userId && c.RoleId == x.r.Id && c.Action == RoleAssignmentChangeAction.Assign)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            var synthId = CompositeGuid(x.pr.PrincipalId, x.pr.RoleId);
            dtos.Add(new AssignmentDto(synthId, x.r.Id, x.r.Name, x.pr.AssignedAt, lastAssign?.ActorUserId));
        }

        var profile = new UserSummaryDto(user.Id, user.DisplayName, user.Email ?? string.Empty, deptCode, user.Active, dtos.Count);
        return new UserDetailDto(profile, dtos);
    }

    public async Task<AssignmentDto> AssignRoleAsync(Guid actorUserId, Guid targetUserId, AssignRoleRequest req, CancellationToken ct = default)
    {
        var role = await db.SharedRoles.FirstOrDefaultAsync(r => r.Code == req.RoleName, ct)
            ?? throw new NotFoundException("Role", req.RoleName);
        var target = await db.SharedPrincipals
            .FirstOrDefaultAsync(p => p.Id == targetUserId && p.Type == SharedPrincipalType.User, ct)
            ?? throw new NotFoundException("User", targetUserId);

        var existing = await db.SharedPrincipalRoles
            .FirstOrDefaultAsync(pr => pr.PrincipalId == target.Id && pr.RoleId == role.Id, ct);
        if (existing is not null)
            throw new ConflictException("user already has this role assignment");

        var pr = new SharedPrincipalRole
        {
            PrincipalId = target.Id,
            RoleId = role.Id,
            InheritToMembers = false,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = actorUserId,
        };
        db.SharedPrincipalRoles.Add(pr);
        db.RoleAssignmentChanges.Add(new RoleAssignmentChange
        {
            ActorUserId = actorUserId,
            TargetUserId = target.Id,
            RoleId = role.Id,
            RoleCodeSnapshot = role.Name,
            Action = RoleAssignmentChangeAction.Assign,
            Scope = AssignmentScope.Tenant,
            ScopeRef = null,
        });
        await db.SaveChangesAsync(ct);

        var synthId = CompositeGuid(pr.PrincipalId, pr.RoleId);
        return new AssignmentDto(synthId, role.Id, role.Name, pr.AssignedAt, actorUserId);
    }

    public async Task RevokeAssignmentAsync(Guid actorUserId, Guid targetUserId, Guid assignmentId, CancellationToken ct = default)
    {
        var rolesForTarget = await db.SharedPrincipalRoles
            .Where(pr => pr.PrincipalId == targetUserId)
            .ToListAsync(ct);
        var pr = rolesForTarget.FirstOrDefault(x => CompositeGuid(x.PrincipalId, x.RoleId) == assignmentId)
            ?? throw new NotFoundException("RoleAssignment", assignmentId);
        var role = await db.SharedRoles.FirstAsync(r => r.Id == pr.RoleId, ct);

        if (role.Code == AdminRoleName)
        {
            if (actorUserId == targetUserId)
                throw new ForbiddenException("cannot revoke your own admin role");

            var totalAdminUsers = await (
                from a in db.SharedPrincipalRoles.AsNoTracking()
                join r in db.SharedRoles.AsNoTracking() on a.RoleId equals r.Id
                where r.Code == AdminRoleName
                select a.PrincipalId
            ).Distinct().CountAsync(ct);
            if (totalAdminUsers <= 1)
                throw new ConflictException("cannot revoke last admin in tenant");
        }

        db.SharedPrincipalRoles.Remove(pr);
        db.RoleAssignmentChanges.Add(new RoleAssignmentChange
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            RoleId = role.Id,
            RoleCodeSnapshot = role.Name,
            Action = RoleAssignmentChangeAction.Revoke,
            Scope = AssignmentScope.Tenant,
            ScopeRef = null,
        });
        await db.SaveChangesAsync(ct);
    }

    // SharedPrincipalRole has no standalone Id (composite key on PrincipalId+RoleId);
    // synthesize a deterministic Guid by XORing the two byte arrays so admin
    // callers have a stable string to round-trip on revoke.
    private static Guid CompositeGuid(Guid principalId, Guid roleId)
    {
        var a = principalId.ToByteArray();
        var b = roleId.ToByteArray();
        var c = new byte[16];
        for (var i = 0; i < 16; i++) c[i] = (byte)(a[i] ^ b[i]);
        return new Guid(c);
    }
}

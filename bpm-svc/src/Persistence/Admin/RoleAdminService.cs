using Bpm.Application.Admin;
using Bpm.Application.Admin.Dtos;
using Bpm.Application.Common.Exceptions;
using Bpm.Domain.Entities.Authz;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Admin;

public sealed class RoleAdminService(AppDbContext db) : IRoleAdminService
{
    private const string AdminRoleCode = "admin";

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var rows = await (
            from r in db.Roles
            select new RoleSummaryDto(
                r.Id, r.Code, r.Name, r.Scope,
                db.RoleAssignments.Count(ra => ra.RoleId == r.Id))
        ).ToListAsync(ct);
        return rows;
    }

    public async Task<PagedResult<UserSummaryDto>> ListUsersAsync(string? q, int page, int pageSize, string? roleCode, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(u => EF.Functions.Like(u.FullName, pattern) || EF.Functions.Like(u.Email, pattern));
        }
        if (!string.IsNullOrWhiteSpace(roleCode))
        {
            var code = roleCode.Trim();
            query = query.Where(u => db.RoleAssignments.Any(ra => ra.PrincipalId == u.Id && ra.Role!.Code == code));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UserSummaryDto>(users.Count);
        foreach (var u in users)
        {
            var roleCount = await db.RoleAssignments.CountAsync(ra => ra.PrincipalId == u.Id, ct);
            items.Add(new UserSummaryDto(u.Id, u.FullName, u.Email, u.Department?.Code, u.IsActive, roleCount));
        }
        return new PagedResult<UserSummaryDto>(items, page, pageSize, total);
    }

    public async Task<UserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User", userId);

        var assignments = await (
            from ra in db.RoleAssignments
            where ra.PrincipalId == userId
            join r in db.Roles on ra.RoleId equals r.Id
            select new { ra, r }).ToListAsync(ct);

        var dtos = new List<AssignmentDto>(assignments.Count);
        foreach (var x in assignments)
        {
            // assignedBy = actor of the most recent Assign action for this assignment's combo
            var lastAssign = await db.RoleAssignmentChanges.AsNoTracking()
                .Where(c => c.TargetUserId == userId && c.RoleId == x.ra.RoleId && c.Action == RoleAssignmentChangeAction.Assign)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);
            dtos.Add(new AssignmentDto(
                x.ra.Id, x.r.Id, x.r.Code, x.r.Name, x.ra.Scope, x.ra.ScopeRef,
                x.ra.CreatedAt, lastAssign?.ActorUserId));
        }

        var profile = new UserSummaryDto(user.Id, user.FullName, user.Email, user.Department?.Code, user.IsActive, dtos.Count);
        return new UserDetailDto(profile, dtos);
    }

    public async Task<AssignmentDto> AssignRoleAsync(Guid actorUserId, Guid targetUserId, AssignRoleRequest req, CancellationToken ct = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == req.RoleCode, ct)
            ?? throw new NotFoundException("Role", req.RoleCode);
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct)
            ?? throw new NotFoundException("User", targetUserId);

        // Idempotent at the (user, role, scope, scopeRef) level
        var scope = req.Scope ?? AssignmentScope.Tenant;
        var existing = await db.RoleAssignments.FirstOrDefaultAsync(ra =>
            ra.PrincipalId == target.Id && ra.RoleId == role.Id && ra.Scope == scope && ra.ScopeRef == req.ScopeRef, ct);
        if (existing is not null)
            throw new ConflictException("user already has this role assignment");

        var ra = new RoleAssignment
        {
            PrincipalId = target.Id,
            RoleId = role.Id,
            Scope = scope,
            ScopeRef = req.ScopeRef,
        };
        db.RoleAssignments.Add(ra);
        db.RoleAssignmentChanges.Add(new RoleAssignmentChange
        {
            ActorUserId = actorUserId,
            TargetUserId = target.Id,
            RoleId = role.Id,
            RoleCodeSnapshot = role.Code,
            Action = RoleAssignmentChangeAction.Assign,
            Scope = scope,
            ScopeRef = req.ScopeRef,
        });
        await db.SaveChangesAsync(ct);

        return new AssignmentDto(ra.Id, role.Id, role.Code, role.Name, scope, req.ScopeRef, ra.CreatedAt, actorUserId);
    }

    public async Task RevokeAssignmentAsync(Guid actorUserId, Guid targetUserId, Guid assignmentId, CancellationToken ct = default)
    {
        var ra = await db.RoleAssignments.FirstOrDefaultAsync(x => x.Id == assignmentId && x.PrincipalId == targetUserId, ct)
            ?? throw new NotFoundException("RoleAssignment", assignmentId);
        var role = await db.Roles.FirstAsync(r => r.Id == ra.RoleId, ct);

        if (role.Code == AdminRoleCode)
        {
            // Self-revoke own last admin?
            if (actorUserId == targetUserId)
            {
                var otherAdminAssignmentsForCaller = await db.RoleAssignments.CountAsync(x =>
                    x.PrincipalId == actorUserId && x.RoleId == role.Id && x.Id != assignmentId, ct);
                if (otherAdminAssignmentsForCaller == 0)
                    throw new ForbiddenException("cannot revoke your own last admin role");
            }

            // Last admin in tenant?
            var totalAdminUsers = await (
                from a in db.RoleAssignments
                join r in db.Roles on a.RoleId equals r.Id
                where r.Code == AdminRoleCode
                select a.PrincipalId
            ).Distinct().CountAsync(ct);
            // After delete, will any admin remain? If target is the only admin user → block.
            var targetHasOtherAdminAssignment = await db.RoleAssignments.CountAsync(x =>
                x.PrincipalId == targetUserId && x.RoleId == role.Id && x.Id != assignmentId, ct) > 0;
            if (totalAdminUsers <= 1 && !targetHasOtherAdminAssignment)
                throw new ConflictException("cannot revoke last admin in tenant");
        }

        db.RoleAssignments.Remove(ra);
        db.RoleAssignmentChanges.Add(new RoleAssignmentChange
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            RoleId = role.Id,
            RoleCodeSnapshot = role.Code,
            Action = RoleAssignmentChangeAction.Revoke,
            Scope = ra.Scope,
            ScopeRef = ra.ScopeRef,
        });
        await db.SaveChangesAsync(ct);
    }
}

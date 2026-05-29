using Bpm.Admin.Application.Flows;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Principals;

public sealed class ChefOrgQueryService : IChefOrgQueryService
{
    private readonly AdminDbContext _db;

    public ChefOrgQueryService(AdminDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct = default)
    {
        var rolesQ = _db.Roles.AsNoTracking().OrderBy(r => r.Name);
        var counts = await _db.PrincipalRoles
            .AsNoTracking()
            .GroupBy(pr => pr.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(x => x.RoleId, x => x.Count);
        var rows = await rolesQ.ToListAsync(ct);
        return rows.Select(r => new RoleSummaryDto(
            r.Id, r.Name, r.Description, countMap.GetValueOrDefault(r.Id, 0), r.IsSystem)).ToList();
    }

    public async Task<IReadOnlyList<PrincipalSummaryDto>> ListPrincipalsAsync(
        string? roleName = null, string? kind = null, string? search = null,
        int take = 100, CancellationToken ct = default)
    {
        var q = _db.Principals.AsNoTracking().Where(p => p.Active);
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!TryParseKind(kind, out var typed))
                throw new FlowLifecycleException($"unknown kind '{kind}' — expected user / dept / group / role");
            q = q.Where(p => p.Type == typed);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = $"%{search}%";
            q = q.Where(p => EF.Functions.Like(p.DisplayName, like) || (p.Email != null && EF.Functions.Like(p.Email, like)));
        }
        if (!string.IsNullOrWhiteSpace(roleName))
        {
            q = from p in q
                join pr in _db.PrincipalRoles on p.Id equals pr.PrincipalId
                join r in _db.Roles on pr.RoleId equals r.Id
                where r.Name == roleName
                select p;
        }

        var capped = Math.Clamp(take, 1, 500);
        var principals = await q
            .OrderBy(p => p.DisplayName)
            .Take(capped)
            .ToListAsync(ct);

        return await EnrichWithRolesAsync(principals, ct);
    }

    public async Task<OrgWalkResultDto> WalkOrgAsync(Guid submitterUserId, string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new FlowLifecycleException("path required");

        var submitter = await _db.Principals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == submitterUserId, ct);
        if (submitter is null)
            throw new FlowLifecycleException($"submitter {submitterUserId} not found");

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var steps = new List<OrgWalkStep>(segments.Length);

        // Walk state: a set of ids + their kind ('user' | 'dept'). Each
        // segment transforms the set.
        IReadOnlyList<Guid> currentIds = new[] { submitterUserId };
        string currentKind = "user";

        foreach (var seg in segments)
        {
            switch (seg)
            {
                case "manager":
                    if (currentKind != "user") throw NotApplicable(seg, currentKind);
                    var managers = await _db.UserManagers.AsNoTracking()
                        .Where(m => currentIds.Contains(m.UserId))
                        .Select(m => m.ManagerUserId).Distinct().ToListAsync(ct);
                    currentIds = managers;
                    currentKind = "user";
                    break;
                case "department":
                    if (currentKind != "user") throw NotApplicable(seg, currentKind);
                    var depts = await _db.UserDepts.AsNoTracking()
                        .Where(d => currentIds.Contains(d.UserId))
                        .Select(d => d.DeptId).Distinct().ToListAsync(ct);
                    currentIds = depts;
                    currentKind = "dept";
                    break;
                case "head":
                    if (currentKind != "dept") throw NotApplicable(seg, currentKind);
                    var heads = await _db.DeptHeads.AsNoTracking()
                        .Where(h => currentIds.Contains(h.DeptId))
                        .Select(h => h.HeadUserId).Distinct().ToListAsync(ct);
                    currentIds = heads;
                    currentKind = "user";
                    break;
                case "parent":
                    if (currentKind != "dept") throw NotApplicable(seg, currentKind);
                    var parents = await _db.DeptParents.AsNoTracking()
                        .Where(p => p.ParentDeptId != null && currentIds.Contains(p.DeptId))
                        .Select(p => p.ParentDeptId!.Value).Distinct().ToListAsync(ct);
                    currentIds = parents;
                    currentKind = "dept";
                    break;
                default:
                    throw new FlowLifecycleException($"unknown path segment '{seg}' — supported: manager / department / head / parent");
            }
            var principals = await LoadPrincipalsAsync(currentIds, ct);
            steps.Add(new OrgWalkStep(seg, currentKind, principals));
        }

        var final = steps.Count > 0
            ? steps[^1].Resolved
            : await LoadPrincipalsAsync(new[] { submitterUserId }, ct);
        return new OrgWalkResultDto(submitterUserId, path, steps, final);
    }

    private static FlowLifecycleException NotApplicable(string seg, string currentKind)
        => new($"segment '{seg}' cannot follow a {currentKind}-typed step");

    private async Task<IReadOnlyList<PrincipalSummaryDto>> LoadPrincipalsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return Array.Empty<PrincipalSummaryDto>();
        var rows = await _db.Principals.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
        return await EnrichWithRolesAsync(rows, ct);
    }

    private async Task<IReadOnlyList<PrincipalSummaryDto>> EnrichWithRolesAsync(IReadOnlyList<Principal> principals, CancellationToken ct)
    {
        if (principals.Count == 0) return Array.Empty<PrincipalSummaryDto>();
        var ids = principals.Select(p => p.Id).ToList();
        var rolesPerPrincipal = await (from pr in _db.PrincipalRoles
                                       join r in _db.Roles on pr.RoleId equals r.Id
                                       where ids.Contains(pr.PrincipalId)
                                       select new { pr.PrincipalId, r.Name })
            .AsNoTracking()
            .ToListAsync(ct);
        var roleMap = rolesPerPrincipal
            .GroupBy(x => x.PrincipalId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).Distinct().ToList());

        return principals.Select(p => new PrincipalSummaryDto(
            p.Id,
            KindString(p.Type),
            p.DisplayName,
            p.Email,
            p.Active,
            roleMap.GetValueOrDefault(p.Id, new List<string>()))).ToList();
    }

    private static string KindString(PrincipalType t) => t switch
    {
        PrincipalType.User => "user",
        PrincipalType.Dept => "dept",
        PrincipalType.Group => "group",
        _ => t.ToString().ToLowerInvariant(),
    };

    private static bool TryParseKind(string raw, out PrincipalType parsed)
    {
        switch (raw.ToLowerInvariant())
        {
            case "user":  parsed = PrincipalType.User; return true;
            case "dept":  parsed = PrincipalType.Dept; return true;
            case "group": parsed = PrincipalType.Group; return true;
            default:      parsed = default; return false;
        }
    }
}

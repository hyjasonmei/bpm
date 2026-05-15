using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Seed;

/// <summary>
/// Idempotent seed for the canonical 13-user / 6-department persona shape
/// that covers every routing path in the 11 demo flows (LEAVE / GEE / GEV /
/// APE / HWP / ITPR / TRQ / TEO / EXTOB / RESIGN / DEPTX).
///
/// <para>
/// Replaces the older <c>OrgFixture</c> (10-user shape, *bpm.local emails).
/// Both <see cref="Bpm.Api.Program"/> startup-seed and the SeedCli
/// (<c>bpm-svc/src/SeedCli</c>) call this same service so the persona list
/// stays consistent regardless of how the DB was bootstrapped.
/// </para>
///
/// <para>
/// Idempotency contract: re-running <see cref="RunAsync"/> against an
/// already-seeded DB is safe — every entity is keyed on a natural code
/// (<see cref="User.Email"/>, <see cref="Department.Code"/>,
/// <see cref="Role.Code"/>) and only inserted when missing.
/// </para>
/// </summary>
public static class PersonaSeedService
{
    public sealed record DepartmentSpec(string Code, string Name);

    public sealed record UserSpec(
        string Email,
        string FullName,
        string DisplayName,
        string DepartmentCode,
        string? ManagerEmail);

    public sealed record RoleSpec(string Code, string Name);

    public sealed record RoleAssignmentSpec(string RoleCode, string PrincipalEmail);

    /// <summary>The 6 departments — Executive sits parallel to the 5 functional ones.</summary>
    public static readonly IReadOnlyList<DepartmentSpec> Departments = new List<DepartmentSpec>
    {
        new("EXEC",        "Executive"),
        new("ENG",         "Engineering"),
        new("HR",          "Human Resources"),
        new("FIN",         "Finance"),
        new("IT",          "Information Technology"),
        new("OPS",         "Operations"),
    };

    /// <summary>
    /// 13 users covering every persona × department combination the demo
    /// flows route to. Email is the natural key (unique).
    /// </summary>
    public static readonly IReadOnlyList<UserSpec> Users = new List<UserSpec>
    {
        // Engineering chain
        new("wilson@acme.test",        "Wilson You 游上毅",     "Wilson",         "ENG",  "yang@acme.test"),
        new("yang@acme.test",          "Yang Wei 楊偉",         "Yang",           "ENG",  "chen@acme.test"),
        new("chen@acme.test",          "Chen VP 陳偉",          "Chen",           "ENG",  "ceo@acme.test"),
        new("ceo@acme.test",           "CEO Liu 劉執行",        "CEO",            "EXEC", null),
        // HR chain
        new("mary@acme.test",          "Mary Chen 陳瑪麗",      "Mary",           "HR",   "hr_lead@acme.test"),
        new("hr_lead@acme.test",       "HR Lead 黃人事",        "HR Lead",        "HR",   "ceo@acme.test"),
        // Finance chain
        new("sue@acme.test",           "Sue Wang 王蘇",         "Sue",            "FIN",  "finance_head@acme.test"),
        new("finance_head@acme.test",  "Finance Head 賴",       "Finance Head",   "FIN",  "ceo@acme.test"),
        // IT chain
        new("lin@acme.test",           "Lin Tu 屠林",           "Lin",            "IT",   "it_lead@acme.test"),
        new("it_lead@acme.test",       "IT Lead 邱資訊",        "IT Lead",        "IT",   "ceo@acme.test"),
        // Operations / Admin chain
        new("pat@acme.test",           "Pat Lo 羅派",           "Pat",            "OPS",  "admin_lead@acme.test"),
        new("admin_lead@acme.test",    "Admin Lead 張總",       "Admin Lead",     "OPS",  "ceo@acme.test"),
        // Tenant-level test admin (no department / no manager)
        new("jason_test@acme.test",    "Jason 測試員",          "Jason",          "EXEC", null),
    };

    /// <summary>
    /// Roles span both old "system" roles needed by existing tests
    /// (<c>admin</c>, <c>designer</c>, <c>viewer</c>, <c>hr</c>) and the
    /// new flow-scoped roles referenced by spec.json ActorRefs
    /// (<c>HR</c>, <c>Finance</c>, <c>IT</c>, <c>Admin</c>, <c>VP</c>,
    /// <c>CEO</c>, <c>Purchase</c>, <c>manager</c>) and by SeedCli docs
    /// (<c>tenant_admin</c>, <c>hr_admin</c>, <c>it_admin</c>).
    /// </summary>
    public static readonly IReadOnlyList<RoleSpec> Roles = new List<RoleSpec>
    {
        // Legacy system roles (kept for backward compatibility with tests
        // and the persona-login mapping in appsettings.Development.json).
        new("admin",        "System Administrator"),
        new("designer",     "Spec Designer"),
        new("viewer",       "Read-only Viewer"),
        new("hr",           "Human Resources (legacy)"),
        // Tenant-scoped root admin role for SeedCli persona shape.
        new("tenant_admin", "Tenant Administrator"),
        // Flow-scoped roles used by the 11 demo specs' ActorRefs. Kept
        // RoleScope.System (not Flow) so a single seed assignment covers
        // every flow that references the role — matches OrgChartReader's
        // System|Flow union semantics.
        new("manager",      "Line Manager"),
        new("HR",           "Human Resources"),
        new("Finance",      "Finance"),
        new("IT",           "Information Technology"),
        new("Admin",        "Administrative Operations"),
        new("Purchase",     "Procurement"),
        new("VP",           "Vice President"),
        new("CEO",          "Chief Executive Officer"),
        new("hr_admin",     "HR Administrator"),
        new("it_admin",     "IT Administrator"),
    };

    /// <summary>
    /// Role → user email assignments. Each row becomes one
    /// <see cref="RoleAssignment"/>. Role codes here MUST exist in <see cref="Roles"/>;
    /// principals MUST exist in <see cref="Users"/>.
    /// </summary>
    public static readonly IReadOnlyList<RoleAssignmentSpec> RoleAssignments = new List<RoleAssignmentSpec>
    {
        // HR routing
        new("HR",            "mary@acme.test"),
        new("HR",            "hr_lead@acme.test"),
        new("hr",            "mary@acme.test"),       // legacy lowercase
        new("hr",            "hr_lead@acme.test"),
        new("hr_admin",      "hr_lead@acme.test"),

        // Finance routing
        new("Finance",       "sue@acme.test"),
        new("Finance",       "finance_head@acme.test"),
        new("Purchase",      "sue@acme.test"),        // doubles as Purchase reviewer

        // IT routing
        new("IT",            "lin@acme.test"),
        new("IT",            "it_lead@acme.test"),
        new("it_admin",      "it_lead@acme.test"),

        // Admin / Operations
        new("Admin",         "pat@acme.test"),
        new("Admin",         "admin_lead@acme.test"),

        // Manager / VP / CEO chain
        new("manager",       "yang@acme.test"),
        new("manager",       "chen@acme.test"),
        new("manager",       "hr_lead@acme.test"),
        new("manager",       "finance_head@acme.test"),
        new("manager",       "it_lead@acme.test"),
        new("manager",       "admin_lead@acme.test"),
        new("VP",            "chen@acme.test"),
        new("CEO",           "ceo@acme.test"),

        // Tenant admins — multi-assignee so dev environments can switch
        // around without losing admin access.
        new("tenant_admin",  "jason_test@acme.test"),
        new("tenant_admin",  "ceo@acme.test"),
        new("tenant_admin",  "hr_lead@acme.test"),
        new("tenant_admin",  "admin_lead@acme.test"),
        new("tenant_admin",  "finance_head@acme.test"),

        // Legacy admin / designer / viewer — keep PersonaSwitchTests etc.
        // green by re-using the new seed's superset of role assignments.
        new("admin",         "jason_test@acme.test"),
        new("admin",         "ceo@acme.test"),
        new("admin",         "pat@acme.test"),         // demo admin persona
        new("admin",         "admin_lead@acme.test"),
        new("designer",      "yang@acme.test"),
        new("designer",      "lin@acme.test"),
        new("designer",      "jason_test@acme.test"),
        new("viewer",        "wilson@acme.test"),
        new("viewer",        "sue@acme.test"),
        new("viewer",        "mary@acme.test"),
    };

    public static async Task RunAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        // 1. Departments — natural key Code.
        var deptByCode = await db.Departments.ToDictionaryAsync(d => d.Code, ct);
        var newDepts = 0;
        foreach (var spec in Departments)
        {
            if (deptByCode.ContainsKey(spec.Code)) continue;
            var dept = new Department
            {
                Id = Guid.NewGuid(),
                Code = spec.Code,
                Name = spec.Name,
                DisplayName = spec.Name,
            };
            db.Departments.Add(dept);
            deptByCode[spec.Code] = dept;
            newDepts++;
        }
        if (newDepts > 0) await db.SaveChangesAsync(ct);

        // 2. Users — natural key Email. First pass: insert without ManagerId
        // so the closure is filled in pass 2. Tracks email→User for both
        // existing and new rows.
        var userByEmail = await db.Users.ToDictionaryAsync(u => u.Email, ct);
        var newUsers = 0;
        foreach (var spec in Users)
        {
            if (userByEmail.ContainsKey(spec.Email)) continue;
            var dept = deptByCode.GetValueOrDefault(spec.DepartmentCode);
            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = spec.Email,
                FullName = spec.FullName,
                DisplayName = spec.DisplayName,
                DepartmentId = dept?.Id,
                IsActive = true,
            };
            db.Users.Add(user);
            userByEmail[spec.Email] = user;
            newUsers++;
        }
        if (newUsers > 0) await db.SaveChangesAsync(ct);

        // Pass 2: ManagerId now that every user row exists.
        var managerLinks = 0;
        foreach (var spec in Users)
        {
            if (spec.ManagerEmail is null) continue;
            var user = userByEmail[spec.Email];
            if (user.ManagerId is not null) continue;
            if (!userByEmail.TryGetValue(spec.ManagerEmail, out var mgr)) continue;
            user.ManagerId = mgr.Id;
            managerLinks++;
        }

        // Department heads — first user keyed off the spec's chain root
        // (CEO/Lead/Head pattern) lands as HeadUserId.
        TrySetDepartmentHead(deptByCode, userByEmail, "EXEC", "ceo@acme.test");
        TrySetDepartmentHead(deptByCode, userByEmail, "ENG",  "chen@acme.test");
        TrySetDepartmentHead(deptByCode, userByEmail, "HR",   "hr_lead@acme.test");
        TrySetDepartmentHead(deptByCode, userByEmail, "FIN",  "finance_head@acme.test");
        TrySetDepartmentHead(deptByCode, userByEmail, "IT",   "it_lead@acme.test");
        TrySetDepartmentHead(deptByCode, userByEmail, "OPS",  "admin_lead@acme.test");

        if (managerLinks > 0 || db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        // 3. Roles — natural key Code.
        var roleByCode = await db.Roles.ToDictionaryAsync(r => r.Code, ct);
        var newRoles = 0;
        foreach (var spec in Roles)
        {
            if (roleByCode.ContainsKey(spec.Code)) continue;
            var role = new Role
            {
                Id = Guid.NewGuid(),
                Code = spec.Code,
                Name = spec.Name,
                Scope = RoleScope.System,
            };
            db.Roles.Add(role);
            roleByCode[spec.Code] = role;
            newRoles++;
        }
        if (newRoles > 0) await db.SaveChangesAsync(ct);

        // 4. Role assignments — composite natural key (RoleId, PrincipalId).
        var existingRas = await db.RoleAssignments
            .Select(ra => new { ra.RoleId, ra.PrincipalId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(Guid, Guid)>(existingRas.Select(x => (x.RoleId, x.PrincipalId)));
        var newRas = 0;
        foreach (var spec in RoleAssignments)
        {
            if (!roleByCode.TryGetValue(spec.RoleCode, out var role)) continue;
            if (!userByEmail.TryGetValue(spec.PrincipalEmail, out var user)) continue;
            if (existingSet.Contains((role.Id, user.Id))) continue;
            db.RoleAssignments.Add(new RoleAssignment
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PrincipalId = user.Id,
                Scope = AssignmentScope.Tenant,
            });
            existingSet.Add((role.Id, user.Id));
            newRas++;
        }
        if (newRas > 0) await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PersonaSeedService: +{Depts} departments, +{Users} users (+{ManagerLinks} manager links), +{Roles} roles, +{Assignments} role assignments",
            newDepts, newUsers, managerLinks, newRoles, newRas);
    }

    private static void TrySetDepartmentHead(
        IDictionary<string, Department> deptByCode,
        IDictionary<string, User> userByEmail,
        string deptCode,
        string headEmail)
    {
        if (!deptByCode.TryGetValue(deptCode, out var dept)) return;
        if (!userByEmail.TryGetValue(headEmail, out var user)) return;
        if (dept.HeadUserId == user.Id) return;
        dept.HeadUserId = user.Id;
    }
}

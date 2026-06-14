using Bpm.Admin.Application.Auth;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Delegations;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Seed;

public static class Seeder
{
    public const string DemoPassword = "flowcook2026";

    /// <summary>
    /// Seeds the admin DB with a representative org graph for dev / demo:
    /// 13 users / 6 depts / 1 group / 14 roles / a few role assignments / 1 delegation.
    /// Sets every seeded user's password to <see cref="DemoPassword"/>.
    /// </summary>
    public static async Task SeedOrgAsync(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AdminDbContext>();
        DbProviderSetup.Configure(builder, DbProviderSetup.ResolveProvider(null), connectionString);
        var options = builder.Options;
        await using var ctx = new AdminDbContext(options);
        var hasher = new PasswordHasher();

        // ---- depts (6) with a 2-level tree
        var deptCompany = AddPrincipal(ctx, PrincipalType.Dept, "Acme Corp");
        var deptEng = AddPrincipal(ctx, PrincipalType.Dept, "Engineering");
        var deptBackend = AddPrincipal(ctx, PrincipalType.Dept, "Backend");
        var deptFrontend = AddPrincipal(ctx, PrincipalType.Dept, "Frontend");
        var deptProduct = AddPrincipal(ctx, PrincipalType.Dept, "Product");
        var deptHR = AddPrincipal(ctx, PrincipalType.Dept, "HR");

        ctx.DeptParents.AddRange(
            new DeptParent { DeptId = deptEng, ParentDeptId = deptCompany },
            new DeptParent { DeptId = deptBackend, ParentDeptId = deptEng },
            new DeptParent { DeptId = deptFrontend, ParentDeptId = deptEng },
            new DeptParent { DeptId = deptProduct, ParentDeptId = deptCompany },
            new DeptParent { DeptId = deptHR, ParentDeptId = deptCompany }
        );

        // ---- users (13)
        var users = new List<(Guid Id, string Email)>();
        string[] userNames = ["Alice","Bob","Carol","Dave","Erin","Frank","Grace","Henry","Iris","Jack","Kate","Leo","Mia"];
        foreach (var name in userNames)
        {
            var email = $"{name.ToLowerInvariant()}@acme.example";
            var id = AddUserWithCredential(ctx, hasher, name, email);
            users.Add((id, email));
        }

        // Assign user → dept (some users in two depts)
        ctx.UserDepts.AddRange(
            new UserDept { UserId = users[0].Id, DeptId = deptBackend, IsPrimary = true },
            new UserDept { UserId = users[1].Id, DeptId = deptBackend, IsPrimary = true },
            new UserDept { UserId = users[2].Id, DeptId = deptFrontend, IsPrimary = true },
            new UserDept { UserId = users[3].Id, DeptId = deptFrontend, IsPrimary = true },
            new UserDept { UserId = users[4].Id, DeptId = deptEng, IsPrimary = true },
            new UserDept { UserId = users[5].Id, DeptId = deptProduct, IsPrimary = true },
            new UserDept { UserId = users[6].Id, DeptId = deptProduct, IsPrimary = true },
            new UserDept { UserId = users[7].Id, DeptId = deptHR, IsPrimary = true },
            new UserDept { UserId = users[8].Id, DeptId = deptHR, IsPrimary = true },
            new UserDept { UserId = users[9].Id, DeptId = deptCompany, IsPrimary = true },
            new UserDept { UserId = users[10].Id, DeptId = deptCompany, IsPrimary = true },
            // user 11 (Leo) is in both Backend and Product (兼任)
            new UserDept { UserId = users[11].Id, DeptId = deptBackend, IsPrimary = true },
            new UserDept { UserId = users[11].Id, DeptId = deptProduct, IsPrimary = false },
            new UserDept { UserId = users[12].Id, DeptId = deptEng, IsPrimary = true }
        );

        // ---- one group with cross-dept members
        var groupSecurity = AddPrincipal(ctx, PrincipalType.Group, "Security Committee");
        ctx.GroupMembers.AddRange(
            new GroupMember { GroupId = groupSecurity, MemberPrincipalId = users[0].Id, MemberType = PrincipalType.User },
            new GroupMember { GroupId = groupSecurity, MemberPrincipalId = users[5].Id, MemberType = PrincipalType.User },
            new GroupMember { GroupId = groupSecurity, MemberPrincipalId = users[7].Id, MemberType = PrincipalType.User }
        );

        // ---- 15 roles
        var roleDefs = new (string Code, string Name, string Desc)[]
        {
            ("APPROVER",       "簽核者",           "通用簽核者"),
            ("SUBMITTER",      "申請人",           "流程申請人"),
            ("REVIEWER",       "審查者",           "覆核 / 審查"),
            ("DIRECTOR",       "總監",             "部門總監"),
            ("VP",             "副總",             "Vice President"),
            ("CEO",            "執行長",           "Chief Executive Officer"),
            ("CFO",            "財務長",           "Chief Financial Officer"),
            ("HR_MANAGER",     "人資主管",         "Human Resources manager"),
            ("PROCUREMENT",    "採購",             "採購審核"),
            ("FINANCE",        "財務",             "財務審核"),
            ("AUDITOR",        "稽核",             "稽核 / 監察"),
            ("FLOW_OWNER",     "流程負責人",       "Flow owner"),
            ("SYSTEM_ADMIN",   "系統管理員",       "System administrator"),
            ("PERSONA_SWITCH", "Persona 切換權限", "可在 sandbox 以他人身分操作（代理切換）"),
            ("WATCHER",        "關注者",           "唯讀關注者"),
        };
        var roleIds = new Dictionary<string, Guid>();
        foreach (var (code, name, desc) in roleDefs)
        {
            var r = new Role
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = desc,
                IsSystem = code is "SYSTEM_ADMIN" or "PERSONA_SWITCH",
            };
            ctx.Roles.Add(r);
            roleIds[code] = r.Id;
        }

        // ---- PrincipalRole sample assignments
        ctx.PrincipalRoles.AddRange(
            // SystemAdmin: assigned direct to user 9 (Jack)
            new PrincipalRole { PrincipalId = users[9].Id, RoleId = roleIds["SYSTEM_ADMIN"], InheritToMembers = false },
            // Persona_Switch: assigned to users[9] and users[10]
            new PrincipalRole { PrincipalId = users[9].Id, RoleId = roleIds["PERSONA_SWITCH"], InheritToMembers = false },
            new PrincipalRole { PrincipalId = users[10].Id, RoleId = roleIds["PERSONA_SWITCH"], InheritToMembers = false },
            // Approver inherits to all engineering staff
            new PrincipalRole { PrincipalId = deptEng, RoleId = roleIds["APPROVER"], InheritToMembers = true },
            // HR_Manager inherits to HR staff
            new PrincipalRole { PrincipalId = deptHR, RoleId = roleIds["HR_MANAGER"], InheritToMembers = true },
            // Reviewer group-wide
            new PrincipalRole { PrincipalId = groupSecurity, RoleId = roleIds["REVIEWER"], InheritToMembers = true },
            // Director assigned to user 0 (Alice) direct
            new PrincipalRole { PrincipalId = users[0].Id, RoleId = roleIds["DIRECTOR"], InheritToMembers = false },
            // Operational approvers the cooked flows resolve: Finance (Frank,
            // the finance persona), Procurement (Grace), VP (Leo). Direct user
            // grants so role:FINANCE / role:PROCUREMENT / role:VP resolve in
            // TEO / PURCHASE_REQUEST / VENDOR_EXPENSE / long-leave.
            new PrincipalRole { PrincipalId = users[5].Id, RoleId = roleIds["FINANCE"], InheritToMembers = false },
            new PrincipalRole { PrincipalId = users[6].Id, RoleId = roleIds["PROCUREMENT"], InheritToMembers = false },
            new PrincipalRole { PrincipalId = users[11].Id, RoleId = roleIds["VP"], InheritToMembers = false }
        );

        // ---- Dept heads (added by unify-user-store change so bpm-svc
        //      IOrgChartReader.GetDepartmentHeadAsync resolves cleanly).
        //      One head per dept; the spec library's dept_head actor reads
        //      this table.
        ctx.DeptHeads.AddRange(
            new DeptHead { DeptId = deptCompany,  HeadUserId = users[9].Id,  AssignedAt = DateTime.UtcNow },   // Jack runs Acme Corp
            new DeptHead { DeptId = deptEng,      HeadUserId = users[4].Id,  AssignedAt = DateTime.UtcNow },   // Erin heads Engineering
            new DeptHead { DeptId = deptBackend,  HeadUserId = users[0].Id,  AssignedAt = DateTime.UtcNow },   // Alice heads Backend
            new DeptHead { DeptId = deptFrontend, HeadUserId = users[2].Id,  AssignedAt = DateTime.UtcNow },   // Carol heads Frontend
            new DeptHead { DeptId = deptProduct,  HeadUserId = users[5].Id,  AssignedAt = DateTime.UtcNow },   // Frank heads Product
            new DeptHead { DeptId = deptHR,       HeadUserId = users[7].Id,  AssignedAt = DateTime.UtcNow }    // Henry heads HR
        );

        // ---- User → direct manager edges. Models the "reports to" line
        //      for spec-library `actor.manager` resolution. Heads of depts
        //      report up to the parent dept's head; top-of-org users have
        //      no manager row (Jack is the CEO).
        ctx.UserManagers.AddRange(
            // Backend members → Alice
            new UserManager { UserId = users[1].Id,  ManagerUserId = users[0].Id, AssignedAt = DateTime.UtcNow }, // Bob → Alice
            new UserManager { UserId = users[11].Id, ManagerUserId = users[0].Id, AssignedAt = DateTime.UtcNow }, // Leo → Alice
            // Frontend members → Carol
            new UserManager { UserId = users[3].Id,  ManagerUserId = users[2].Id, AssignedAt = DateTime.UtcNow }, // Dave → Carol
            // Engineering members → Erin
            new UserManager { UserId = users[12].Id, ManagerUserId = users[4].Id, AssignedAt = DateTime.UtcNow }, // Mia → Erin
            // Product members → Frank
            new UserManager { UserId = users[6].Id,  ManagerUserId = users[5].Id, AssignedAt = DateTime.UtcNow }, // Grace → Frank
            // HR members → Henry
            new UserManager { UserId = users[8].Id,  ManagerUserId = users[7].Id, AssignedAt = DateTime.UtcNow }, // Iris → Henry
            // Dept heads → CEO (Jack)
            new UserManager { UserId = users[0].Id,  ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }, // Alice → Jack
            new UserManager { UserId = users[2].Id,  ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }, // Carol → Jack
            new UserManager { UserId = users[4].Id,  ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }, // Erin → Jack
            new UserManager { UserId = users[5].Id,  ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }, // Frank → Jack
            new UserManager { UserId = users[7].Id,  ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }, // Henry → Jack
            // Kate (admin) reports to Jack
            new UserManager { UserId = users[10].Id, ManagerUserId = users[9].Id, AssignedAt = DateTime.UtcNow }
        );

        // ---- 1 delegation example (Alice → Bob next week)
        ctx.Delegations.Add(new Delegation
        {
            Id = Guid.NewGuid(),
            DelegatorPrincipalId = users[0].Id,
            DelegateToUserId = users[1].Id,
            StartAt = DateTime.UtcNow.AddDays(2),
            EndAt = DateTime.UtcNow.AddDays(9),
            Active = true,
            Reason = "seeded sample delegation",
        });

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Truncates every admin-owned table (in FK-safe reverse order) after
    /// ensuring the schema is current. The shared SQLite file's bpm-owned
    /// tables are left untouched — wipe those via bpm-svc. Shared by the
    /// SeedCli <c>clear</c>/<c>seed</c> commands and the admin Reset endpoint.
    /// </summary>
    public static async Task ClearOrgAsync(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AdminDbContext>();
        DbProviderSetup.Configure(builder, DbProviderSetup.ResolveProvider(null), connectionString);
        var options = builder.Options;
        await using var ctx = new AdminDbContext(options);
        await ctx.Database.MigrateAsync();
        // Per-DbSet ExecuteDeleteAsync (NOT raw SQL): EF emits the
        // provider-correct quoted identifier for each table. The old raw
        // `DELETE FROM Admin_UserSessions` worked on SQLite (case-insensitive
        // identifiers) but 500'd on Postgres — Npgsql created the tables as
        // case-sensitive "Admin_UserSessions", while unquoted raw SQL folds
        // to lowercase `admin_usersessions` → relation does not exist. Same
        // FK-safe reverse order as before. Mirrors bpm-svc ResetService.
        await ctx.UserSessions.ExecuteDeleteAsync();
        await ctx.UserCredentials.ExecuteDeleteAsync();
        await ctx.AuditEvents.ExecuteDeleteAsync();
        await ctx.Delegations.ExecuteDeleteAsync();
        await ctx.PrincipalRoles.ExecuteDeleteAsync();
        await ctx.GroupMembers.ExecuteDeleteAsync();
        await ctx.DeptHeads.ExecuteDeleteAsync();
        await ctx.UserManagers.ExecuteDeleteAsync();
        await ctx.UserDepts.ExecuteDeleteAsync();
        await ctx.DeptParents.ExecuteDeleteAsync();
        await ctx.Flows.ExecuteDeleteAsync();
        await ctx.Roles.ExecuteDeleteAsync();
        await ctx.Principals.ExecuteDeleteAsync();
    }

    /// <summary>Clear + re-seed the org graph in one call — the canonical
    /// "back to demo init" for admin-owned identity tables.</summary>
    public static async Task ResetOrgAsync(string connectionString)
    {
        await ClearOrgAsync(connectionString);
        await SeedOrgAsync(connectionString);
    }

    private static Guid AddPrincipal(AdminDbContext ctx, PrincipalType type, string name)
    {
        var p = new Principal
        {
            Id = Guid.NewGuid(),
            Type = type,
            DisplayName = name,
            Active = true,
        };
        ctx.Principals.Add(p);
        return p.Id;
    }

    private static Guid AddUserWithCredential(AdminDbContext ctx, PasswordHasher hasher, string name, string email)
    {
        var p = new Principal
        {
            Id = Guid.NewGuid(),
            Type = PrincipalType.User,
            DisplayName = name,
            Email = email,
            Active = true,
        };
        ctx.Principals.Add(p);
        ctx.UserCredentials.Add(new UserCredential
        {
            UserId = p.Id,
            PasswordHash = hasher.Hash(DemoPassword),
            CreatedAt = DateTime.UtcNow,
        });
        return p.Id;
    }
}

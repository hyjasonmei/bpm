using Bpm.Admin.Application.Auth;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Delegations;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.SeedCli;

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
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connectionString).Options;
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

        // ---- 14 roles
        string[] roleNames =
        {
            "Approver","Submitter","Reviewer","Director","CEO","CFO","HR_Manager",
            "Procurement","Finance","Auditor","FlowOwner","SystemAdmin","Persona_Switch","Watcher"
        };
        var roleIds = new Dictionary<string, Guid>();
        foreach (var name in roleNames)
        {
            var r = new Role { Id = Guid.NewGuid(), Name = name, IsSystem = name is "SystemAdmin" or "Persona_Switch" };
            ctx.Roles.Add(r);
            roleIds[name] = r.Id;
        }

        // ---- PrincipalRole sample assignments
        ctx.PrincipalRoles.AddRange(
            // SystemAdmin: assigned direct to user 9 (Jack)
            new PrincipalRole { PrincipalId = users[9].Id, RoleId = roleIds["SystemAdmin"], InheritToMembers = false },
            // Persona_Switch: assigned to users[9] and users[10]
            new PrincipalRole { PrincipalId = users[9].Id, RoleId = roleIds["Persona_Switch"], InheritToMembers = false },
            new PrincipalRole { PrincipalId = users[10].Id, RoleId = roleIds["Persona_Switch"], InheritToMembers = false },
            // Approver inherits to all engineering staff
            new PrincipalRole { PrincipalId = deptEng, RoleId = roleIds["Approver"], InheritToMembers = true },
            // HR_Manager inherits to HR staff
            new PrincipalRole { PrincipalId = deptHR, RoleId = roleIds["HR_Manager"], InheritToMembers = true },
            // Reviewer group-wide
            new PrincipalRole { PrincipalId = groupSecurity, RoleId = roleIds["Reviewer"], InheritToMembers = true },
            // Director assigned to user 0 (Alice) direct
            new PrincipalRole { PrincipalId = users[0].Id, RoleId = roleIds["Director"], InheritToMembers = false }
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

using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Roles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Application.Tests;

public class EffectiveRoleResolverTests
{
    private static (AdminDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AdminDbContext>().UseSqlite(connection).Options;
        var ctx = new AdminDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, connection);
    }

    private static Guid AddPrincipal(AdminDbContext ctx, PrincipalType type, string name)
    {
        var p = new Principal { Type = type, DisplayName = name };
        ctx.Principals.Add(p);
        ctx.SaveChanges();
        return p.Id;
    }

    private static Guid AddRole(AdminDbContext ctx, string name)
    {
        var r = new Role { Id = Guid.NewGuid(), Name = name };
        ctx.Roles.Add(r);
        ctx.SaveChanges();
        return r.Id;
    }

    private static void Assign(AdminDbContext ctx, Guid principalId, Guid roleId, bool inherit)
    {
        ctx.PrincipalRoles.Add(new PrincipalRole
        {
            PrincipalId = principalId,
            RoleId = roleId,
            InheritToMembers = inherit,
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Direct_Only()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var role = AddRole(ctx, "Approver");
            Assign(ctx, user, role, inherit: false);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Single(result);
            var only = result.First();
            Assert.Equal(role, only.RoleId);
            Assert.Equal(user, only.SourcePrincipalId);
            Assert.False(only.ViaInherit);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Dept_Inherit_True_Reaches_User()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var dept = AddPrincipal(ctx, PrincipalType.Dept, "Engineering");
            ctx.UserDepts.Add(new UserDept { UserId = user, DeptId = dept, IsPrimary = true });
            ctx.SaveChanges();
            var role = AddRole(ctx, "Approver");
            Assign(ctx, dept, role, inherit: true);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Single(result);
            var only = result.First();
            Assert.Equal(role, only.RoleId);
            Assert.Equal(dept, only.SourcePrincipalId);
            Assert.True(only.ViaInherit);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Dept_Inherit_False_Does_Not_Reach_User()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var dept = AddPrincipal(ctx, PrincipalType.Dept, "Engineering");
            ctx.UserDepts.Add(new UserDept { UserId = user, DeptId = dept });
            ctx.SaveChanges();
            var role = AddRole(ctx, "DeptInbox");
            Assign(ctx, dept, role, inherit: false);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Empty(result);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Dept_Ancestor_Inherit_Reaches_User()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var dept = AddPrincipal(ctx, PrincipalType.Dept, "Backend");
            var parentDept = AddPrincipal(ctx, PrincipalType.Dept, "Engineering");
            ctx.UserDepts.Add(new UserDept { UserId = user, DeptId = dept });
            ctx.DeptParents.Add(new DeptParent { DeptId = dept, ParentDeptId = parentDept });
            ctx.SaveChanges();
            var role = AddRole(ctx, "Approver");
            Assign(ctx, parentDept, role, inherit: true);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Single(result);
            Assert.Equal(parentDept, result.First().SourcePrincipalId);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Group_Inherit_Reaches_User()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var group = AddPrincipal(ctx, PrincipalType.Group, "Q3 Project");
            ctx.GroupMembers.Add(new GroupMember
            {
                GroupId = group,
                MemberPrincipalId = user,
                MemberType = PrincipalType.User,
            });
            ctx.SaveChanges();
            var role = AddRole(ctx, "ProjectMember");
            Assign(ctx, group, role, inherit: true);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Single(result);
            Assert.Equal(group, result.First().SourcePrincipalId);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Nested_Group_Inherit_Reaches_User()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var inner = AddPrincipal(ctx, PrincipalType.Group, "Inner");
            var outer = AddPrincipal(ctx, PrincipalType.Group, "Outer");
            // user is direct member of inner; inner is member of outer
            ctx.GroupMembers.AddRange(
                new GroupMember { GroupId = inner, MemberPrincipalId = user, MemberType = PrincipalType.User },
                new GroupMember { GroupId = outer, MemberPrincipalId = inner, MemberType = PrincipalType.Group }
            );
            ctx.SaveChanges();
            var role = AddRole(ctx, "OuterRole");
            Assign(ctx, outer, role, inherit: true);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Single(result);
            Assert.Equal(outer, result.First().SourcePrincipalId);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Mixed_Direct_Dept_Group_All_Aggregate()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var user = AddPrincipal(ctx, PrincipalType.User, "Alice");
            var dept = AddPrincipal(ctx, PrincipalType.Dept, "Engineering");
            var group = AddPrincipal(ctx, PrincipalType.Group, "Security WG");
            ctx.UserDepts.Add(new UserDept { UserId = user, DeptId = dept });
            ctx.GroupMembers.Add(new GroupMember
            {
                GroupId = group,
                MemberPrincipalId = user,
                MemberType = PrincipalType.User,
            });
            ctx.SaveChanges();

            var rDirect = AddRole(ctx, "DirectRole");
            var rDept = AddRole(ctx, "DeptRole");
            var rGroup = AddRole(ctx, "GroupRole");
            Assign(ctx, user, rDirect, inherit: false);
            Assign(ctx, dept, rDept, inherit: true);
            Assign(ctx, group, rGroup, inherit: true);

            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(user);

            Assert.Equal(3, result.Count);
            Assert.Contains(result, e => e.RoleId == rDirect && !e.ViaInherit);
            Assert.Contains(result, e => e.RoleId == rDept && e.ViaInherit);
            Assert.Contains(result, e => e.RoleId == rGroup && e.ViaInherit);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Unknown_User_Returns_Empty()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var resolver = new EffectiveRoleResolver(ctx);
            var result = await resolver.GetEffectiveRolesAsync(Guid.NewGuid());
            Assert.Empty(result);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}

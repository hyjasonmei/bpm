using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Bpm.Admin.Persistence.Principals;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class GroupMembershipCycleTests
{
    private static (AdminDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AdminDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AdminDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    private static async Task<Guid> AddGroupAsync(AdminDbContext ctx, string name)
    {
        var g = new Principal { Type = PrincipalType.Group, DisplayName = name };
        ctx.Principals.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    [Fact]
    public async Task Self_Membership_Throws()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var svc = new GroupMembershipService(ctx);
            var g1 = await AddGroupAsync(ctx, "G1");
            await Assert.ThrowsAsync<GroupCycleException>(
                () => svc.AddMemberAsync(g1, g1, PrincipalType.Group));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Direct_Cycle_Throws()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var svc = new GroupMembershipService(ctx);
            var g1 = await AddGroupAsync(ctx, "G1");
            var g2 = await AddGroupAsync(ctx, "G2");
            await svc.AddMemberAsync(g1, g2, PrincipalType.Group);

            // attempting g2 → g1 should fail (would form g1 ↔ g2)
            await Assert.ThrowsAsync<GroupCycleException>(
                () => svc.AddMemberAsync(g2, g1, PrincipalType.Group));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task Indirect_Cycle_Throws()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var svc = new GroupMembershipService(ctx);
            var g1 = await AddGroupAsync(ctx, "G1");
            var g2 = await AddGroupAsync(ctx, "G2");
            var g3 = await AddGroupAsync(ctx, "G3");
            await svc.AddMemberAsync(g1, g2, PrincipalType.Group);
            await svc.AddMemberAsync(g2, g3, PrincipalType.Group);

            // attempting g3 → g1 should fail (would form g1 → g2 → g3 → g1)
            await Assert.ThrowsAsync<GroupCycleException>(
                () => svc.AddMemberAsync(g3, g1, PrincipalType.Group));
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }

    [Fact]
    public async Task User_Member_Of_Group_OK()
    {
        var (ctx, conn) = CreateContext();
        try
        {
            var svc = new GroupMembershipService(ctx);
            var g1 = await AddGroupAsync(ctx, "G1");
            var u1 = new Principal { Type = PrincipalType.User, DisplayName = "Alice" };
            ctx.Principals.Add(u1);
            await ctx.SaveChangesAsync();

            await svc.AddMemberAsync(g1, u1.Id, PrincipalType.User);
            Assert.Single(ctx.GroupMembers);
        }
        finally { ctx.Dispose(); conn.Dispose(); }
    }
}

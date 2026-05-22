using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Admin.Persistence.Tests;

public class PrincipalSmokeTests
{
    private static (AdminDbContext ctx, SqliteConnection conn) CreateInMemoryContext()
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

    [Fact]
    public void Can_Create_Read_Update_SoftDelete_Principal()
    {
        var (ctx, conn) = CreateInMemoryContext();
        try
        {
            var p = new Principal
            {
                Type = PrincipalType.User,
                DisplayName = "Alice",
                Email = "alice@example.com",
            };
            ctx.Principals.Add(p);
            ctx.SaveChanges();

            Assert.NotEqual(Guid.Empty, p.Id);
            Assert.NotEqual(default(DateTime), p.CreatedAt);
            Assert.NotEqual(default(DateTime), p.UpdatedAt);
            Assert.Null(p.DeletedAt);

            var fetched = ctx.Principals.Single();
            Assert.Equal("Alice", fetched.DisplayName);

            fetched.DisplayName = "Alice 2";
            var originalUpdatedAt = fetched.UpdatedAt;
            Thread.Sleep(5);
            ctx.SaveChanges();
            Assert.True(fetched.UpdatedAt > originalUpdatedAt, "UpdatedAt should be stamped on modify");

            fetched.DeletedAt = DateTime.UtcNow;
            ctx.SaveChanges();

            Assert.Empty(ctx.Principals);
            Assert.Single(ctx.Principals.IgnoreQueryFilters());
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }

    [Fact]
    public void Can_Filter_By_Principal_Type()
    {
        var (ctx, conn) = CreateInMemoryContext();
        try
        {
            ctx.Principals.AddRange(
                new Principal { Type = PrincipalType.User, DisplayName = "Alice" },
                new Principal { Type = PrincipalType.Dept, DisplayName = "Engineering" },
                new Principal { Type = PrincipalType.Group, DisplayName = "Q3 Team" }
            );
            ctx.SaveChanges();

            Assert.Equal(1, ctx.Principals.Count(p => p.Type == PrincipalType.User));
            Assert.Equal(1, ctx.Principals.Count(p => p.Type == PrincipalType.Dept));
            Assert.Equal(1, ctx.Principals.Count(p => p.Type == PrincipalType.Group));
        }
        finally
        {
            ctx.Dispose();
            conn.Dispose();
        }
    }
}

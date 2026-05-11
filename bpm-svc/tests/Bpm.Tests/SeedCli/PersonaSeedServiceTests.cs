using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Seed;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.SeedCli;

/// <summary>
/// PR-L4 §4 — PersonaSeedService (the new 13-user shape that replaced
/// OrgFixture). Verifies idempotency, role assignments, and the manager
/// chain wired by the seed.
/// </summary>
public sealed class PersonaSeedServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    public PersonaSeedServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var clock = new StubClock();
        var interceptor = new AuditSaveChangesInterceptor(clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Seed_creates_13_users_6_departments_and_role_assignments()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);

        Assert.Equal(13, await db.Users.CountAsync());
        Assert.Equal(6, await db.Departments.CountAsync());
        Assert.Equal(PersonaSeedService.Roles.Count, await db.Roles.CountAsync());
        Assert.True(await db.RoleAssignments.CountAsync() >= PersonaSeedService.RoleAssignments.Count);
    }

    [Fact]
    public async Task Seed_is_idempotent_on_rerun()
    {
        await using (var db = new AppDbContext(_options))
        {
            await PersonaSeedService.RunAsync(db, NullLogger.Instance);
        }

        int usersAfterFirst, raAfterFirst, deptsAfterFirst, rolesAfterFirst;
        await using (var db = new AppDbContext(_options))
        {
            usersAfterFirst = await db.Users.CountAsync();
            deptsAfterFirst = await db.Departments.CountAsync();
            rolesAfterFirst = await db.Roles.CountAsync();
            raAfterFirst = await db.RoleAssignments.CountAsync();
        }

        await using (var db = new AppDbContext(_options))
        {
            await PersonaSeedService.RunAsync(db, NullLogger.Instance);
        }

        await using var read = new AppDbContext(_options);
        Assert.Equal(usersAfterFirst, await read.Users.CountAsync());
        Assert.Equal(deptsAfterFirst, await read.Departments.CountAsync());
        Assert.Equal(rolesAfterFirst, await read.Roles.CountAsync());
        Assert.Equal(raAfterFirst, await read.RoleAssignments.CountAsync());
    }

    [Fact]
    public async Task Seed_wires_manager_chain_to_ceo()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);

        var wilson = await db.Users.SingleAsync(u => u.Email == "wilson@acme.test");
        var yang   = await db.Users.SingleAsync(u => u.Email == "yang@acme.test");
        var chen   = await db.Users.SingleAsync(u => u.Email == "chen@acme.test");
        var ceo    = await db.Users.SingleAsync(u => u.Email == "ceo@acme.test");

        Assert.Equal(yang.Id, wilson.ManagerId);
        Assert.Equal(chen.Id, yang.ManagerId);
        Assert.Equal(ceo.Id, chen.ManagerId);
        Assert.Null(ceo.ManagerId);
    }

    [Fact]
    public async Task Seed_assigns_HR_role_to_mary_and_hr_lead()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);

        var hrPrincipals = await (
            from ra in db.RoleAssignments
            join r in db.Roles on ra.RoleId equals r.Id
            join u in db.Users on ra.PrincipalId equals u.Id
            where r.Code == "HR"
            select u.Email
        ).ToListAsync();

        Assert.Contains("mary@acme.test", hrPrincipals);
        Assert.Contains("hr_lead@acme.test", hrPrincipals);
    }

    [Fact]
    public async Task Seed_assigns_finance_it_admin_roles_to_their_persona_users()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);

        var byRole = await (
            from ra in db.RoleAssignments
            join r in db.Roles on ra.RoleId equals r.Id
            join u in db.Users on ra.PrincipalId equals u.Id
            select new { Role = r.Code, Email = u.Email }
        ).ToListAsync();

        Assert.Contains(byRole, x => x.Role == "Finance" && x.Email == "sue@acme.test");
        Assert.Contains(byRole, x => x.Role == "Finance" && x.Email == "finance_head@acme.test");
        Assert.Contains(byRole, x => x.Role == "IT" && x.Email == "lin@acme.test");
        Assert.Contains(byRole, x => x.Role == "IT" && x.Email == "it_lead@acme.test");
        Assert.Contains(byRole, x => x.Role == "Admin" && x.Email == "pat@acme.test");
        Assert.Contains(byRole, x => x.Role == "Admin" && x.Email == "admin_lead@acme.test");
        Assert.Contains(byRole, x => x.Role == "VP" && x.Email == "chen@acme.test");
        Assert.Contains(byRole, x => x.Role == "CEO" && x.Email == "ceo@acme.test");
        Assert.Contains(byRole, x => x.Role == "tenant_admin" && x.Email == "jason_test@acme.test");
    }

    [Fact]
    public async Task Seed_sets_department_heads_for_each_department()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);

        var depts = await db.Departments.ToListAsync();
        Assert.All(depts, d => Assert.NotNull(d.HeadUserId));
    }
}

using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Common;

/// <summary>
/// The IncludeSubDepts flag must behave identically here (routing) and in
/// admin-svc's EffectiveRoleResolver (display): a role on a PARENT dept
/// reaches child-dept members only when the assignment opted in. See
/// bpm-admin-svc EffectiveRoleResolverTests for the admin-side twin cases.
/// </summary>
public sealed class PrincipalDirectoryIncludeSubDeptsTests : IDisposable
{
    private static readonly Guid Bob        = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    private static readonly Guid ChildDept  = Guid.Parse("00000000-0000-0000-0000-00000000c41d");
    private static readonly Guid ParentDept = Guid.Parse("00000000-0000-0000-0000-00000000fa7e");
    private static readonly Guid RoleLegal  = Guid.Parse("00000000-0000-0000-0000-000000000a11");

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    public PrincipalDirectoryIncludeSubDeptsTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (
    Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL, DisplayName TEXT NOT NULL,
    Email TEXT NULL, Active INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL);
CREATE TABLE Admin_Roles (
    Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '', Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL);
CREATE TABLE Admin_PrincipalRoles (
    PrincipalId TEXT NOT NULL, RoleId TEXT NOT NULL, InheritToMembers INTEGER NOT NULL,
    IncludeSubDepts INTEGER NOT NULL DEFAULT 0, AssignedAt TEXT NOT NULL, AssignedByUserId TEXT NULL,
    PRIMARY KEY (PrincipalId, RoleId));
CREATE TABLE Admin_UserDepts (
    UserId TEXT NOT NULL, DeptId TEXT NOT NULL, IsPrimary INTEGER NOT NULL, PRIMARY KEY (UserId, DeptId));
CREATE TABLE Admin_DeptParents (DeptId TEXT NOT NULL PRIMARY KEY, ParentDeptId TEXT NULL);
CREATE TABLE Admin_GroupMembers (GroupId TEXT NOT NULL, MemberPrincipalId TEXT NOT NULL, MemberType INTEGER NOT NULL, PRIMARY KEY (GroupId, MemberPrincipalId));");

        var now = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);
        db.SharedPrincipals.AddRange(
            new SharedPrincipal { Id = Bob, Type = SharedPrincipalType.User, DisplayName = "Bob", Email = "bob@acme.example", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = ChildDept, Type = SharedPrincipalType.Dept, DisplayName = "Backend", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = ParentDept, Type = SharedPrincipalType.Dept, DisplayName = "Engineering", Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedRoles.Add(new SharedRole { Id = RoleLegal, Code = "LEGAL", Name = "法務", IsSystem = false });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Bob, DeptId = ChildDept, IsPrimary = true });
        db.SharedDeptParents.Add(new SharedDeptParent { DeptId = ChildDept, ParentDeptId = ParentDept });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private void Grant(Guid principalId, bool inherit, bool includeSubDepts)
    {
        using var db = new AppDbContext(_options);
        db.SharedPrincipalRoles.Add(new SharedPrincipalRole
        {
            PrincipalId = principalId,
            RoleId = RoleLegal,
            InheritToMembers = inherit,
            IncludeSubDepts = includeSubDepts,
            AssignedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task ParentDept_Grant_Without_IncludeSubDepts_Does_Not_Route_To_ChildDept_User()
    {
        Grant(ParentDept, inherit: true, includeSubDepts: false);
        await using var db = new AppDbContext(_options);
        var dir = new PrincipalDirectory(db);

        Assert.Empty(await dir.GetUsersInRoleAsync("LEGAL"));
        Assert.DoesNotContain("LEGAL", await dir.GetRoleCodesForUserAsync(Bob));
    }

    [Fact]
    public async Task ParentDept_Grant_With_IncludeSubDepts_Routes_To_ChildDept_User()
    {
        Grant(ParentDept, inherit: true, includeSubDepts: true);
        await using var db = new AppDbContext(_options);
        var dir = new PrincipalDirectory(db);

        Assert.Contains(Bob, await dir.GetUsersInRoleAsync("LEGAL"));
        Assert.Contains("LEGAL", await dir.GetRoleCodesForUserAsync(Bob));
    }

    [Fact]
    public async Task OwnDept_Grant_Needs_Only_InheritToMembers()
    {
        Grant(ChildDept, inherit: true, includeSubDepts: false);
        await using var db = new AppDbContext(_options);
        var dir = new PrincipalDirectory(db);

        Assert.Contains(Bob, await dir.GetUsersInRoleAsync("LEGAL"));
        Assert.Contains("LEGAL", await dir.GetRoleCodesForUserAsync(Bob));
    }
}

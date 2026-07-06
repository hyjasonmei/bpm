using Bpm.Application.Features.LEAVE.V1;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.LEAVE.V1;
using Bpm.Persistence;
using Bpm.Persistence.Features.LEAVE.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bpm.Tests.Features.LEAVE.V1;

/// <summary>
/// E2E for PR-N1: drive a real LEAVE V1 Submit through the production
/// <see cref="LEAVE_V1_LeaveService"/> wired up with a real
/// <see cref="FileNotifyDispatcher"/> writing to a temp file. Asserts
/// the dispatcher actually appended the rendered notification.
/// </summary>
public sealed class LEAVE_V1_NotifyDispatchE2ETests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly string _notifyPath;

    private static readonly Guid Bob   = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000a11c");
    private static readonly Guid Vera  = Guid.Parse("00000000-0000-0000-0000-00000000ce40");
    private static readonly Guid Henry = Guid.Parse("00000000-0000-0000-0000-0000000000b5");
    private static readonly Guid HqDept = Guid.Parse("00000000-0000-0000-0000-00000000d557");
    private static readonly Guid RoleHr = Guid.Parse("00000000-0000-0000-0000-000000000a01");
    private static readonly Guid RoleVp = Guid.Parse("00000000-0000-0000-0000-000000000a02");

    public LEAVE_V1_NotifyDispatchE2ETests()
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
        CreateAdminTables(db);
        SeedActors(db);

        _notifyPath = Path.Combine(
            Path.GetTempPath(),
            $"bpm-notify-e2e-{Guid.NewGuid():N}.txt");
    }

    public void Dispose()
    {
        _conn.Dispose();
        if (File.Exists(_notifyPath)) File.Delete(_notifyPath);
    }

    [Fact]
    public async Task Submit_writes_notification_to_file()
    {
        await using var db = new AppDbContext(_options);
        var dispatcher = new FileNotifyDispatcher(
            Options.Create(new NotifyDispatcherOptions { FilePath = _notifyPath }),
            NullLogger<FileNotifyDispatcher>.Instance);
        var svc = new LEAVE_V1_LeaveService(
            new LEAVE_V1_CaseStore(db), new OrgChartReader(db), new StubClock(), NullLogger<LEAVE_V1_LeaveService>.Instance, dispatcher, new TestActorAuthorizer(), new Bpm.Persistence.Common.Directory.PrincipalDirectory(db));

        var c = await svc.SubmitAsync(new(
            SubmitterUserId: Bob,
            LeaveType: "特休",
            StartDate: new DateOnly(2026, 5, 11),
            EndDate:   new DateOnly(2026, 5, 13),
            Reason: "Family trip",
            CertFileId: null), default);

        Assert.Equal(LEAVE_V1_CaseStatus.PendingManager, c.Status);
        Assert.True(File.Exists(_notifyPath), $"notify file should exist at {_notifyPath}");

        var contents = await File.ReadAllTextAsync(_notifyPath);
        // Source id pinpoints which spec notification fired.
        Assert.Contains("LEAVE_V1.notify_assign_manager", contents);
        // Subject rendered with the submitter's display name (Bob).
        Assert.Contains("Bob", contents);
        // Body comes from LEAVE_V1_NotificationTemplates.RenderAssignManager.
        Assert.Contains("subject:", contents);
        Assert.Contains("body:", contents);
        // Recipient is the manager (Alice) — resolved via email + display name.
        Assert.Contains("Alice", contents);
        Assert.Contains("alice@acme.example", contents);
        // Channels from the LEAVE V1 wire-up.
        Assert.Contains("email", contents);
        Assert.Contains("in_app", contents);
        // Case id context tag.
        Assert.Contains(c.Id.ToString(), contents);
    }

    [Fact]
    public async Task Multiple_dispatches_append_not_overwrite()
    {
        await using var db = new AppDbContext(_options);
        var dispatcher = new FileNotifyDispatcher(
            Options.Create(new NotifyDispatcherOptions { FilePath = _notifyPath }),
            NullLogger<FileNotifyDispatcher>.Instance);
        var svc = new LEAVE_V1_LeaveService(
            new LEAVE_V1_CaseStore(db), new OrgChartReader(db), new StubClock(), NullLogger<LEAVE_V1_LeaveService>.Instance, dispatcher, new TestActorAuthorizer(), new Bpm.Persistence.Common.Directory.PrincipalDirectory(db));

        await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13),
            "first", null), default);
        await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12),
            "second", null), default);

        var contents = await File.ReadAllTextAsync(_notifyPath);
        // Two distinct entries separated by `---` headers.
        var headerCount = contents.Split("\n---\n").Length - 1;
        Assert.True(headerCount >= 1,
            $"expected ≥1 '---' separator between entries; full file:\n{contents}");
        // Both submit notifications landed.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(contents, "LEAVE_V1.notify_assign_manager").Count;
        Assert.Equal(2, occurrences);
    }

    // ------------------------------------------------------------
    // Schema + seed (mirrors LEAVE_V1_LeaveServiceTests fixture).
    // ------------------------------------------------------------

    private static void CreateAdminTables(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (
    Id TEXT NOT NULL PRIMARY KEY,
    Type INTEGER NOT NULL,
    DisplayName TEXT NOT NULL,
    Email TEXT NULL,
    Active INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    DeletedAt TEXT NULL
);
CREATE TABLE Admin_Roles (
    Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '',
    Name TEXT NOT NULL,
    IsSystem INTEGER NOT NULL,
    Description TEXT NULL
);
CREATE TABLE Admin_PrincipalRoles (
    PrincipalId TEXT NOT NULL,
    RoleId TEXT NOT NULL,
    InheritToMembers INTEGER NOT NULL,
    IncludeSubDepts INTEGER NOT NULL DEFAULT 0,
    AssignedAt TEXT NOT NULL,
    AssignedByUserId TEXT NULL,
    PRIMARY KEY (PrincipalId, RoleId)
);
CREATE TABLE Admin_DeptParents (
    DeptId TEXT NOT NULL PRIMARY KEY,
    ParentDeptId TEXT NULL
);
CREATE TABLE Admin_UserManagers (
    UserId TEXT NOT NULL PRIMARY KEY,
    ManagerUserId TEXT NOT NULL,
    AssignedAt TEXT NOT NULL
);
CREATE TABLE Admin_UserDepts (
    UserId TEXT NOT NULL,
    DeptId TEXT NOT NULL,
    IsPrimary INTEGER NOT NULL,
    PRIMARY KEY (UserId, DeptId)
);
CREATE TABLE Admin_DeptHeads (
    DeptId TEXT NOT NULL PRIMARY KEY,
    HeadUserId TEXT NOT NULL,
    AssignedAt TEXT NOT NULL
);");
    }

    private void SeedActors(AppDbContext db)
    {
        var now = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        db.SharedPrincipals.AddRange(
            new SharedPrincipal { Id = Bob,   Type = SharedPrincipalType.User, DisplayName = "Bob",   Email = "bob@acme.example",   Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Alice, Type = SharedPrincipalType.User, DisplayName = "Alice", Email = "alice@acme.example", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Vera,  Type = SharedPrincipalType.User, DisplayName = "Vera",  Email = "vera@acme.example",  Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Henry, Type = SharedPrincipalType.User, DisplayName = "Henry", Email = "henry@acme.example", Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedRoles.AddRange(
            new SharedRole { Id = RoleHr, Code = "HR_MANAGER", Name = "HR", IsSystem = false },
            new SharedRole { Id = RoleVp, Code = "VP", Name = "VP", IsSystem = false });
        db.SharedPrincipalRoles.AddRange(
            new SharedPrincipalRole { PrincipalId = Henry, RoleId = RoleHr, InheritToMembers = false, AssignedAt = now },
            new SharedPrincipalRole { PrincipalId = Vera,  RoleId = RoleVp, InheritToMembers = false, AssignedAt = now });
        db.SharedUserManagers.Add(new SharedUserManager { UserId = Bob, ManagerUserId = Alice, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Bob, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Vera, AssignedAt = now });
        db.SaveChanges();
    }
}

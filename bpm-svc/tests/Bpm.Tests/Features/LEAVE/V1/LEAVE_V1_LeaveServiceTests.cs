using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Persistence;
using Bpm.Application.Features.LEAVE.V1;
using Bpm.Domain.Features.LEAVE.V1;
using Bpm.Persistence.Features.LEAVE.V1;
using Bpm.Persistence.Org;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.LEAVE.V1;

/// <summary>
/// Unit tests for the LEAVE V1 state machine. Uses in-memory SQLite and
/// hand-rolled <c>CREATE TABLE</c> statements for the Admin_* tables
/// (which carry <c>ExcludeFromMigrations</c> in production — admin-svc
/// owns the real schema).
/// </summary>
public sealed class LEAVE_V1_LeaveServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    // Seed actor ids — fixed so individual asserts are easier to read.
    private static readonly Guid Bob    = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    private static readonly Guid Alice  = Guid.Parse("00000000-0000-0000-0000-00000000a11c"); // manager
    private static readonly Guid Vera   = Guid.Parse("00000000-0000-0000-0000-00000000ce40"); // VP + dept head
    private static readonly Guid Henry  = Guid.Parse("00000000-0000-0000-0000-0000000000b5"); // HR
    private static readonly Guid HqDept = Guid.Parse("00000000-0000-0000-0000-00000000d557");
    private static readonly Guid RoleHr = Guid.Parse("00000000-0000-0000-0000-000000000a01");
    private static readonly Guid RoleVp = Guid.Parse("00000000-0000-0000-0000-000000000a02");

    public LEAVE_V1_LeaveServiceTests()
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
    }

    public void Dispose() => _conn.Dispose();

    // ------------------------------------------------------------
    // Submit
    // ------------------------------------------------------------

    [Fact]
    public async Task Submit_happy_path_routes_to_manager()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        var c = await svc.SubmitAsync(new(
            SubmitterUserId: Bob,
            LeaveType: "特休",
            StartDate: new DateOnly(2026, 5, 11),  // Mon
            EndDate:   new DateOnly(2026, 5, 13),  // Wed
            Reason: "Family trip",
            CertFileId: null), default);

        Assert.Equal(LEAVE_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Alice, c.ManagerUserId);
        Assert.Equal(Alice, c.CurrentAssigneeUserId);
        Assert.Equal(3m, c.Days);
    }

    [Fact]
    public async Task Submit_with_病假_and_no_cert_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await svc.SubmitAsync(new(Bob, "病假",
                new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 11),
                "感冒", CertFileId: null), default);
        });
    }

    [Fact]
    public async Task Submit_with_inverted_range_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await svc.SubmitAsync(new(Bob, "特休",
                new DateOnly(2026, 5, 13), new DateOnly(2026, 5, 11),
                "test", null), default);
        });
    }

    [Fact]
    public async Task Submit_without_manager_conflicts()
    {
        // Wipe manager rows to simulate a user with no manager.
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_UserManagers");
        }

        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ConflictException>(async () =>
        {
            await svc.SubmitAsync(new(Bob, "特休",
                new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13),
                "x", null), default);
        });
    }

    // ------------------------------------------------------------
    // Gateway / decisions
    // ------------------------------------------------------------

    [Fact]
    public async Task ManagerApprove_short_leave_routes_to_HR()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13), "trip", null), default);

        var after = await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: "ok", default);

        Assert.Equal(LEAVE_V1_CaseStatus.PendingHr, after.Status);
        Assert.Null(after.HrUserId);                           // shared role queue — no single designated user
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.Equal("HR_MANAGER", after.CurrentAssigneeRoleCode);
        Assert.True(after.ManagerApproved);
    }

    [Fact]
    public async Task ManagerApprove_long_leave_routes_to_VP_via_dept_head()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "事假",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10),  // 8 business days
            "出國", null), default);

        Assert.True(c.Days >= 7m);
        var after = await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);

        Assert.Equal(LEAVE_V1_CaseStatus.PendingVp, after.Status);
        Assert.Equal(Vera, after.VpUserId);              // dept head primary
        Assert.Equal(Vera, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task ManagerReject_terminates_as_Rejected()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13), "trip", null), default);

        var after = await svc.ManagerDecisionAsync(c.Id, Alice, approve: false, comment: "too busy", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Rejected, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task ManagerDecision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13), "trip", null), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            await svc.ManagerDecisionAsync(c.Id, Henry /* HR, not manager */,
                approve: true, comment: null, default);
        });
    }

    [Fact]
    public async Task VpApprove_routes_to_HR()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "事假",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10), "long trip", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);

        var after = await svc.VpDecisionAsync(c.Id, Vera, approve: true, comment: "fine", default);

        Assert.Equal(LEAVE_V1_CaseStatus.PendingHr, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);              // HR is a shared role queue
        Assert.Equal("HR_MANAGER", after.CurrentAssigneeRoleCode);
    }

    [Fact]
    public async Task VpReject_terminates_as_Rejected()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "事假",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10), "long trip", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);
        var after = await svc.VpDecisionAsync(c.Id, Vera, approve: false, comment: "no", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Rejected, after.Status);
    }

    [Fact]
    public async Task HrArchive_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13), "trip", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);

        var after = await svc.HrArchiveAsync(c.Id, Henry, "logged in payroll", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Completed, after.Status);
        Assert.NotNull(after.CompletedAt);
        Assert.Equal("logged in payroll", after.ArchiveNote);
    }

    [Fact]
    public async Task HrArchive_by_non_hr_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13), "trip", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            await svc.HrArchiveAsync(c.Id, Bob, "trying to self-archive", default);
        });
    }

    // ------------------------------------------------------------
    // Actor resolution (incl. role:VP fallback)
    // ------------------------------------------------------------

    [Fact]
    public async Task ResolveVpApprover_uses_dept_head_when_present()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var vp = await svc.ResolveVpApproverAsync(Bob, default);
        Assert.Equal(Vera, vp);
    }

    [Fact]
    public async Task ResolveVpApprover_falls_back_to_role_VP_when_no_dept_head()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_DeptHeads");
        }

        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var vp = await svc.ResolveVpApproverAsync(Bob, default);
        Assert.Equal(Vera, vp);  // Vera also holds role:VP
    }

    [Fact]
    public async Task ResolveDeptHead_skips_when_head_is_submitter_themselves()
    {
        // Promote Bob to be his own dept head — simulates the "head can't approve
        // their own leave" edge case the resolver must handle.
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE Admin_DeptHeads SET HeadUserId = {0} WHERE DeptId = {1}",
                Bob, HqDept);
        }

        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var head = await svc.ResolveDeptHeadAsync(Bob, default);
        Assert.Null(head);
    }

    // ------------------------------------------------------------
    // Computation + templates
    // ------------------------------------------------------------

    [Theory]
    [InlineData("2026-05-11", "2026-05-13", 3)]   // Mon–Wed
    [InlineData("2026-05-09", "2026-05-10", 0)]   // Sat–Sun
    [InlineData("2026-05-08", "2026-05-11", 2)]   // Fri + Mon, skips weekend
    [InlineData("2026-06-01", "2026-06-10", 8)]   // 10-day span = 8 business days
    public void ComputeBusinessDays_matches_expected(string s, string e, int expected)
    {
        var actual = LEAVE_V1_LeaveService.ComputeBusinessDays(DateOnly.Parse(s), DateOnly.Parse(e));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NotificationTemplate_AssignManager_renders_subject_and_body()
    {
        var r = LEAVE_V1_NotificationTemplates.RenderAssignManager(
            applicantName: "Bob",
            days: 3m,
            leaveType: "特休",
            start: new DateOnly(2026, 5, 11),
            end:   new DateOnly(2026, 5, 13),
            reason: "Family trip",
            caseUrl: "/cases/leave/abc");

        Assert.Contains("Bob", r.Subject);
        Assert.Contains("3", r.Subject);
        Assert.Contains("特休", r.Subject);
        Assert.Contains("2026-05-11", r.Body);
        Assert.Contains("/cases/leave/abc", r.Body);
    }

    [Fact]
    public void NotificationTemplate_Complete_renders_subject_and_body()
    {
        var r = LEAVE_V1_NotificationTemplates.RenderComplete(
            submittedAt: new DateTime(2026, 5, 11, 9, 0, 0, DateTimeKind.Utc),
            days: 3m,
            leaveType: "特休");

        Assert.Equal("您的請假已備案", r.Subject);
        Assert.Contains("3 天 特休", r.Body);
    }

    // ------------------------------------------------------------
    // End-to-end: spec test cases tc_1 / tc_2 / tc_3
    // ------------------------------------------------------------

    [Fact]
    public async Task E2E_tc_1_short_leave_completes_via_manager_then_HR()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "特休",
            new DateOnly(2026, 5, 11), new DateOnly(2026, 5, 13),
            "家裡有事", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);
        var done = await svc.HrArchiveAsync(c.Id, Henry, "logged", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Completed, done.Status);
    }

    [Fact]
    public async Task E2E_tc_2_long_leave_completes_via_manager_then_VP_then_HR()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "事假",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 10),
            "出國", null), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);
        await svc.VpDecisionAsync(c.Id, Vera, approve: true, comment: null, default);
        var done = await svc.HrArchiveAsync(c.Id, Henry, "logged", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Completed, done.Status);
        Assert.NotNull(done.VpDecisionAt);
        Assert.True(done.VpApproved);
    }

    [Fact]
    public async Task E2E_tc_3_sick_leave_with_cert_completes()
    {
        var certId = Guid.NewGuid();
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(new(Bob, "病假",
            new DateOnly(2026, 5, 15), new DateOnly(2026, 5, 15),
            "流感", CertFileId: certId), default);
        await svc.ManagerDecisionAsync(c.Id, Alice, approve: true, comment: null, default);
        var done = await svc.HrArchiveAsync(c.Id, Henry, "cert filed", default);

        Assert.Equal(LEAVE_V1_CaseStatus.Completed, done.Status);
        Assert.Equal(certId, done.CertFileId);
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static LEAVE_V1_LeaveService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new LEAVE_V1_CaseStore(db), new OrgChartReader(db), new StubClock(), NullLogger<LEAVE_V1_LeaveService>.Instance, notify ?? new NullNotifyDispatcher(),
               new TestActorAuthorizer(new Dictionary<Guid, IReadOnlySet<string>>
               {
                   [Henry] = new HashSet<string> { "HR_MANAGER" },
                   [Vera]  = new HashSet<string> { "VP" },
               }),
               new Bpm.Persistence.Common.Directory.PrincipalDirectory(db));

    /// <summary>No-op dispatcher for unit tests that don't care about notify output.</summary>
    private sealed class NullNotifyDispatcher : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>
    /// CREATE TABLE the Admin_* subset SharedIdentity configurations exclude
    /// from EF migrations. The shape must match what
    /// <see cref="SharedPrincipalConfiguration"/> et al. expect.
    /// </summary>
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
    AssignedAt TEXT NOT NULL,
    AssignedByUserId TEXT NULL,
    PRIMARY KEY (PrincipalId, RoleId)
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

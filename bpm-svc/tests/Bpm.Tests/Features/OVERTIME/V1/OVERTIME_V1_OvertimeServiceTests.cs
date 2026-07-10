using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.OVERTIME.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.OVERTIME.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.OVERTIME.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.OVERTIME.V1;

/// <summary>
/// Unit tests for the OVERTIME V1 state machine. Wires the real EF-backed
/// CaseStore + PrincipalDirectory + OrgChartReader against an in-memory SQLite so
/// the pass exercises the same Application↔Persistence seam production uses.
/// </summary>
public sealed class OVERTIME_V1_OvertimeServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    // Seed actors — semantic matches for the bundle's sample-org.
    private static readonly Guid Emily  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike   = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Emily's manager
    private static readonly Guid Henry  = Guid.Parse("55555555-5555-5555-5555-555555555555"); // HR role holder
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid HrRole = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Same calendar month for all OvertimeDate values so the monthly-cumulative
    // gateway is deterministic.
    private static readonly DateOnly D = new(2026, 5, 12);

    public OVERTIME_V1_OvertimeServiceTests()
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

    // ============================================================
    // Submit
    // ============================================================

    [Fact]
    public async Task Submit_happy_path_routes_to_manager()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);

        Assert.Equal(OVERTIME_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.ManagerUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(Emily, c.SubmitterUserId);
        Assert.Equal("加班趕專案結案", c.OvertimeReason);
    }

    [Fact]
    public async Task Submit_without_reason_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = NewInput(hours: 3m) with { OvertimeReason = "   " };
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_with_negative_hours_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = NewInput(hours: -2m);
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_without_manager_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_UserManagers");
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.SubmitAsync(NewInput(hours: 3m), default));
    }

    [Fact]
    public async Task Submit_when_manager_is_self_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE Admin_UserManagers SET ManagerUserId = {0} WHERE UserId = {1}", Emily, Emily);
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.SubmitAsync(NewInput(hours: 3m), default));
    }

    // ============================================================
    // Manager decision + gateway
    // ============================================================

    [Fact]
    public async Task Manager_approve_over_threshold_routes_to_hr()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);

        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.PendingHr, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);            // shared HR role queue
        Assert.Equal("HR_MANAGER", after.CurrentAssigneeRoleCode);
        Assert.Equal(25m, after.MonthlyHours);
        Assert.True(after.ManagerApproved);
        Assert.Null(after.CompletedAt);
    }

    [Fact]
    public async Task Manager_approve_under_threshold_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);

        var after = await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.Null(after.CurrentAssigneeRoleCode);
        Assert.Equal(5m, after.MonthlyHours);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task Manager_approve_null_hours_completes_as_zero()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: null), default);

        var after = await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, after.Status);
        Assert.Equal(0m, after.MonthlyHours);
    }

    [Fact]
    public async Task Manager_approve_cumulative_month_over_threshold_routes_to_hr()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        // Prior completed overtime this month: 18h (≤20 so it auto-completed).
        var prior = await svc.SubmitAsync(NewInput(hours: 18m), default);
        var priorDone = await svc.ApproveByManagerAsync(prior.Id, Mike, null, default);
        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, priorDone.Status);

        // New 5h request: alone ≤20, but cumulative 18+5 = 23 > 20 → HR.
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        Assert.Equal(OVERTIME_V1_CaseStatus.PendingHr, after.Status);
        Assert.Equal(23m, after.MonthlyHours);
    }

    [Fact]
    public async Task Manager_reject_is_terminal()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);

        var after = await svc.RejectByManagerAsync(c.Id, Mike, "工時不合理", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.RejectedByManager, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.False(after.ManagerApproved);
        Assert.Equal("工時不合理", after.ManagerComment);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task Manager_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByManagerAsync(c.Id, Henry, "not the manager", default));
    }

    [Fact]
    public async Task Manager_approve_over_threshold_without_hr_member_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_PrincipalRoles");
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ApproveByManagerAsync(c.Id, Mike, null, default));
    }

    // ============================================================
    // HR decision
    // ============================================================

    [Fact]
    public async Task Hr_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        var done = await svc.ApproveByHrAsync(c.Id, Henry, "符合規定", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, done.Status);
        Assert.Equal(Henry, done.HrUserId);
        Assert.True(done.HrApproved);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Hr_reject_is_terminal()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        var after = await svc.RejectByHrAsync(c.Id, Henry, "超過月上限", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.RejectedByHr, after.Status);
        Assert.False(after.HrApproved);
        Assert.Equal("超過月上限", after.HrComment);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task Hr_decision_by_non_hr_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 25m), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);

        // Mike is a manager, not an HR role holder — cannot act on the HR queue.
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByHrAsync(c.Id, Mike, "not HR", default));
    }

    // ============================================================
    // Withdraw (baseline)
    // ============================================================

    [Fact]
    public async Task Withdraw_by_submitter_cancels()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);

        var after = await svc.CancelAsync(c.Id, Emily, default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Cancelled, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task Withdraw_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () => await svc.CancelAsync(c.Id, Mike, default));
    }

    [Fact]
    public async Task Withdraw_when_terminal_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 5m), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);   // → Completed

        await Assert.ThrowsAsync<ConflictException>(async () => await svc.CancelAsync(c.Id, Emily, default));
    }

    // ============================================================
    // Notification templates
    // ============================================================

    [Fact]
    public void Template_AssignManager_renders_fields()
    {
        var r = OVERTIME_V1_NotificationTemplates.RenderAssignManager(
            "Emily", new DateTime(2026, 5, 11, 9, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 5, 12), 25m, "趕結案");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("2026-05-12", r.Body);
        Assert.Contains("25 小時", r.Body);
        Assert.Contains("趕結案", r.Body);
    }

    [Fact]
    public void Template_ManagerApproved_renders()
    {
        var r = OVERTIME_V1_NotificationTemplates.RenderManagerApproved(
            "Emily", new DateTime(2026, 5, 11, 9, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 5, 12), 6m);
        Assert.Contains("已核准", r.Subject);
        Assert.Contains("6 小時", r.Body);
    }

    [Fact]
    public void Template_ManagerRejected_renders_reason()
    {
        var r = OVERTIME_V1_NotificationTemplates.RenderManagerRejected(
            "Emily", new DateTime(2026, 5, 11, 9, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 5, 12), 25m, "工時不合理");
        Assert.Contains("主管退回", r.Subject);
        Assert.Contains("工時不合理", r.Body);
    }

    [Fact]
    public void Template_HrRejected_renders_reason()
    {
        var r = OVERTIME_V1_NotificationTemplates.RenderHrRejected(
            "Emily", new DateTime(2026, 5, 11, 9, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 5, 12), 25m, "超過月上限");
        Assert.Contains("人資退回", r.Subject);
        Assert.Contains("超過月上限", r.Body);
    }

    [Fact]
    public void Template_FullyApproved_renders()
    {
        var r = OVERTIME_V1_NotificationTemplates.RenderFullyApproved(
            "Emily", new DateOnly(2026, 5, 12), 25m, "趕結案");
        Assert.Contains("全程核准", r.Subject);
        Assert.Contains("25 小時", r.Body);
        Assert.Contains("趕結案", r.Body);
    }

    // ============================================================
    // E2E
    // ============================================================

    [Fact]
    public async Task E2E_over_threshold_submit_manager_hr_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 30m), default);
        var atHr = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(OVERTIME_V1_CaseStatus.PendingHr, atHr.Status);
        var done = await svc.ApproveByHrAsync(c.Id, Henry, "approved", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, done.Status);
        Assert.NotNull(done.ManagerDecisionAt);
        Assert.NotNull(done.HrDecisionAt);
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(Mike, done.ManagerUserId);
        Assert.Equal(Henry, done.HrUserId);
    }

    [Fact]
    public async Task E2E_under_threshold_submit_manager_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(hours: 8m), default);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);

        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.HrUserId);
        Assert.NotNull(done.CompletedAt);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static OVERTIME_V1_OvertimeService NewService(AppDbContext db, INotifyDispatcher? notify = null)
    {
        IOVERTIME_V1_CaseStore store = new OVERTIME_V1_CaseStore(db);
        IOrgChartReader org = new OrgChartReader(db);
        IPrincipalDirectory directory = new PrincipalDirectory(db);
        return new OVERTIME_V1_OvertimeService(
            store, org, directory, new StubClock(),
            NullLogger<OVERTIME_V1_OvertimeService>.Instance,
            notify ?? new NullNotifyDispatcher(),
            new TestActorAuthorizer(new Dictionary<Guid, IReadOnlySet<string>>
            {
                [Henry] = new HashSet<string> { "HR_MANAGER" },
            }));
    }

    private static OVERTIME_V1_OvertimeService.SubmitInput NewInput(decimal? hours)
        => new(
            SubmitterUserId: Emily,
            OvertimeDate: D,
            StartTime: "18:00",
            EndTime: "21:00",
            EstimatedHours: hours,
            OvertimeReason: "加班趕專案結案");

    private sealed class NullNotifyDispatcher : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

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
            new SharedPrincipal { Id = Emily, Type = SharedPrincipalType.User, DisplayName = "Emily Employee", Email = "employee@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Mike,  Type = SharedPrincipalType.User, DisplayName = "Mike Manager",   Email = "manager@acme.tld",  Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Henry, Type = SharedPrincipalType.User, DisplayName = "Henry HR",       Email = "hr@acme.tld",       Active = true, CreatedAt = now, UpdatedAt = now });

        db.SharedRoles.Add(new SharedRole { Id = HrRole, Code = "HR_MANAGER", Name = "人資主管", IsSystem = false });
        db.SharedPrincipalRoles.Add(new SharedPrincipalRole
        {
            PrincipalId = Henry, RoleId = HrRole, InheritToMembers = false, AssignedAt = now,
        });

        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });

        db.SaveChanges();
    }
}

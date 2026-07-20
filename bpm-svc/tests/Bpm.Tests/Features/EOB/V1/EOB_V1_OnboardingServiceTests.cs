using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.EOB.V1;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.EOB.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.EOB.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.EOB.V1;

/// <summary>Unit tests for the EOB V1 state machine (manager approval → setup).</summary>
public sealed class EOB_V1_OnboardingServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike  = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Vera  = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public EOB_V1_OnboardingServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var interceptor = new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).AddInterceptors(interceptor).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        CreateAdminTables(db);
        SeedActors(db);
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Submit_routes_to_manager()
    {
        await using var db = new AppDbContext(_options);
        var c = await NewService(db).SubmitAsync(NewInput(), default);
        Assert.Equal(EOB_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal("Raven", c.FirstName);
    }

    [Fact]
    public async Task Submit_missing_name_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { FirstName = " " }, default));
    }

    [Fact]
    public async Task Submit_missing_cost_center_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { CostCenter = "" }, default));
    }

    [Fact]
    public async Task Manager_approve_routes_to_setup()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(EOB_V1_CaseStatus.PendingSetup, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "missing info", default);
        Assert.Equal(EOB_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Wrong_manager_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () => await svc.ApproveByManagerAsync(c.Id, Vera, null, default));
    }

    [Fact]
    public async Task Setup_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        var done = await svc.CompleteSetupAsync(c.Id, Emily, new[]
        {
            new EOB_V1_SetupTask("Employee Account Setup", "Complete"),
            new EOB_V1_SetupTask("Add member into DL", "Complete"),
        }, default);
        Assert.Equal(EOB_V1_CaseStatus.Completed, done.Status);
        Assert.Equal(2, done.SetupTasks.Count);
        Assert.Equal(Emily, done.SetupByUserId);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Setup_by_wrong_user_is_forbidden()
    {
        // Regression cover for the guard normalization: the setup stage is
        // guarded on CurrentAssigneeUserId (= the submitter Emily). Mike, the
        // approving manager, is not the assigned coordinator — deny him. The
        // guard runs before task validation, so empty tasks still surface 403.
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.CompleteSetupAsync(c.Id, Mike, Array.Empty<EOB_V1_SetupTask>(), default));
    }

    [Fact]
    public async Task Setup_empty_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.CompleteSetupAsync(c.Id, Emily, Array.Empty<EOB_V1_SetupTask>(), default));
    }

    [Fact]
    public async Task Setup_before_approval_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.CompleteSetupAsync(c.Id, Emily, new[] { new EOB_V1_SetupTask("x", "Complete") }, default));
    }

    [Fact]
    public async Task Resubmit_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, NewInput() with { BusinessTitle = "Senior Engineer" }, default);
        Assert.Equal(EOB_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal("Senior Engineer", after.BusinessTitle);
    }

    [Fact]
    public void NotificationTemplate_Assign_renders()
    {
        var r = EOB_V1_NotificationTemplates.RenderAssign("Emily", "新進員工 — Raven Wang", "/cases/eob/x");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("/cases/eob/x", r.Body);
    }

    [Fact]
    public async Task E2E_full_path_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var done = await svc.CompleteSetupAsync(c.Id, Emily, new[] { new EOB_V1_SetupTask("Seat", "Complete") }, default);
        Assert.Equal(EOB_V1_CaseStatus.Completed, done.Status);
    }

    private static EOB_V1_OnboardingService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new EOB_V1_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<EOB_V1_OnboardingService>.Instance, notify ?? new NullNotifyDispatcher(), new TestActorAuthorizer());

    private static EOB_V1_OnboardingService.SubmitInput NewInput()
        => new(Emily, "Raven", "Wang", "Cloud Platform Engineer", "APAC", new DateOnly(2026, 7, 1),
               "Yes", "TWT.1746G", "230517-AP-0001", new DateOnly(2026, 5, 17), new DateOnly(2027, 5, 16));

    private sealed class NullNotifyDispatcher : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static void CreateAdminTables(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL, DisplayName TEXT NOT NULL, Email TEXT NULL, Active INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL);
CREATE TABLE Admin_Roles (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '', Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL);
CREATE TABLE Admin_PrincipalRoles (PrincipalId TEXT NOT NULL, RoleId TEXT NOT NULL, InheritToMembers INTEGER NOT NULL, IncludeSubDepts INTEGER NOT NULL DEFAULT 0, AssignedAt TEXT NOT NULL, AssignedByUserId TEXT NULL, PRIMARY KEY (PrincipalId, RoleId));
CREATE TABLE Admin_DeptParents (
    DeptId TEXT NOT NULL PRIMARY KEY,
    ParentDeptId TEXT NULL
);
CREATE TABLE Admin_UserManagers (UserId TEXT NOT NULL PRIMARY KEY, ManagerUserId TEXT NOT NULL, AssignedAt TEXT NOT NULL);
CREATE TABLE Admin_UserDepts (UserId TEXT NOT NULL, DeptId TEXT NOT NULL, IsPrimary INTEGER NOT NULL, PRIMARY KEY (UserId, DeptId));
CREATE TABLE Admin_DeptHeads (DeptId TEXT NOT NULL PRIMARY KEY, HeadUserId TEXT NOT NULL, AssignedAt TEXT NOT NULL);");
    }

    private void SeedActors(AppDbContext db)
    {
        var now = new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc);
        db.SharedPrincipals.AddRange(
            new SharedPrincipal { Id = Emily, Type = SharedPrincipalType.User, DisplayName = "Emily Employee", Email = "employee@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Mike,  Type = SharedPrincipalType.User, DisplayName = "Mike Manager",   Email = "manager@acme.tld",  Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Vera,  Type = SharedPrincipalType.User, DisplayName = "Vera VP",        Email = "vp@acme.tld",       Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });
        db.SaveChanges();
    }
}

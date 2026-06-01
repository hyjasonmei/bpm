using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.APE.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.APE.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.APE.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.APE.V1;

/// <summary>
/// Unit tests for the APE V1 state machine (single submitter.manager
/// approval). Real EF-backed store + directory + org reader against
/// in-memory SQLite.
/// </summary>
public sealed class APE_V1_AdvancePaymentServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike   = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Emily's manager
    private static readonly Guid Vera   = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public APE_V1_AdvancePaymentServiceTests()
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
    public async Task Submit_happy_path_routes_to_manager()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        Assert.Equal(APE_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.ManagerUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(50000m, c.Amount);
        Assert.Equal("NTD", c.Currency);
    }

    [Fact]
    public async Task Submit_missing_charge_department_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(NewSubmitInput() with { ChargeDepartment = " " }, default));
    }

    [Fact]
    public async Task Submit_zero_amount_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(NewSubmitInput() with { Amount = 0m }, default));
    }

    [Fact]
    public async Task Submit_missing_dates_reject()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(NewSubmitInput() with { ExpectReceiveDate = default }, default));
    }

    [Fact]
    public async Task Submit_without_manager_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_UserManagers");
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.SubmitAsync(NewSubmitInput(), default));
    }

    [Fact]
    public async Task Manager_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(APE_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.ManagerApproved);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "need receipt plan", default);
        Assert.Equal(APE_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.ManagerApproved);
    }

    [Fact]
    public async Task Decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByManagerAsync(c.Id, Vera, "nope", default));
    }

    [Fact]
    public async Task Resubmit_after_reject_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "lower the amount", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput() with { Amount = 30000m }, default);
        Assert.Equal(APE_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Null(after.ManagerApproved);
        Assert.Equal(30000m, after.Amount);
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ResubmitAsync(c.Id, Mike, NewSubmitInput() with { SubmitterUserId = Mike }, default));
    }

    [Fact]
    public async Task Resubmit_when_not_in_resubmit_state_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput(), default));
    }

    [Fact]
    public void NotificationTemplate_Assign_renders_summary()
    {
        var r = APE_V1_NotificationTemplates.RenderAssign("Emily", "預支現金 — NTD 50,000（Backend）", "/cases/ape/x");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("預支現金", r.Body);
        Assert.Contains("/cases/ape/x", r.Body);
    }

    [Fact]
    public async Task E2E_reject_resubmit_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "fix dept", default);
        await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput() with { ChargeDepartment = "Platform" }, default);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok now", default);
        Assert.Equal(APE_V1_CaseStatus.Completed, done.Status);
        Assert.Equal(2, done.RoundCount);
        Assert.Equal("Platform", done.ChargeDepartment);
    }

    private static APE_V1_AdvancePaymentService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new APE_V1_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<APE_V1_AdvancePaymentService>.Instance, notify ?? new NullNotifyDispatcher());

    private static APE_V1_AdvancePaymentService.SubmitInput NewSubmitInput()
        => new(Emily, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 1), "Backend", "No",
               "Conference travel advance", 50000m, "NTD");

    private sealed class NullNotifyDispatcher : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static void CreateAdminTables(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL, DisplayName TEXT NOT NULL, Email TEXT NULL, Active INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL);
CREATE TABLE Admin_Roles (Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL);
CREATE TABLE Admin_PrincipalRoles (PrincipalId TEXT NOT NULL, RoleId TEXT NOT NULL, InheritToMembers INTEGER NOT NULL, AssignedAt TEXT NOT NULL, AssignedByUserId TEXT NULL, PRIMARY KEY (PrincipalId, RoleId));
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

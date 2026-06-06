using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.TEO.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.TEO.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.TEO.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.TEO.V1;

/// <summary>Unit tests for the TEO V1 two-stage state machine (manager + Finance).</summary>
public sealed class TEO_V1_TravelExpenseServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike  = Guid.Parse("22222222-2222-2222-2222-222222222222"); // manager
    private static readonly Guid Frank = Guid.Parse("00000000-0000-0000-0000-00000000f4a1"); // Finance role
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FinanceRole = Guid.Parse("c3a098d8-d377-4f45-a1bb-0cc23386a7c1");

    public TEO_V1_TravelExpenseServiceTests()
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
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        Assert.Equal(TEO_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Single(c.ExpenseItems);
    }

    [Fact]
    public async Task Submit_without_items_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(NewInput() with { ExpenseItems = Array.Empty<TEO_V1_ExpenseItem>() }, default));
    }

    [Fact]
    public async Task Submit_without_travel_request_no_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(NewInput() with { TravelRequestNo = " " }, default));
    }

    [Fact]
    public async Task Manager_approve_routes_to_finance()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(TEO_V1_CaseStatus.PendingFinance, after.Status);
        Assert.Equal(Frank, after.FinanceUserId);
        Assert.Equal(Frank, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "fix receipts", default);
        Assert.Equal(TEO_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Manager_approve_without_finance_member_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_PrincipalRoles");
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.ApproveByManagerAsync(c.Id, Mike, null, default));
    }

    [Fact]
    public async Task Finance_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        var done = await svc.ApproveByFinanceAsync(c.Id, Frank, "approved", default);
        Assert.Equal(TEO_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Finance_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        var after = await svc.RejectByFinanceAsync(c.Id, Frank, "amount mismatch", default);
        Assert.Equal(TEO_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Wrong_user_decision_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () => await svc.ApproveByManagerAsync(c.Id, Frank, null, default));
    }

    [Fact]
    public async Task Resubmit_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, NewInput() with { TravelRequestNo = "TW-TRQ-2" }, default);
        Assert.Equal(TEO_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Null(after.ManagerApproved);
        Assert.Equal("TW-TRQ-2", after.TravelRequestNo);
    }

    [Fact]
    public void NotificationTemplate_Assign_renders()
    {
        var r = TEO_V1_NotificationTemplates.RenderAssign("Emily", "差旅費用核銷 — 2 筆", "/cases/teo/x");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("/cases/teo/x", r.Body);
    }

    [Fact]
    public async Task E2E_reject_resubmit_full_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "fix", default);
        await svc.ResubmitAsync(c.Id, Emily, NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var done = await svc.ApproveByFinanceAsync(c.Id, Frank, "ok", default);
        Assert.Equal(TEO_V1_CaseStatus.Completed, done.Status);
        Assert.Equal(2, done.RoundCount);
    }

    private static TEO_V1_TravelExpenseService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new TEO_V1_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<TEO_V1_TravelExpenseService>.Instance, notify ?? new NullNotifyDispatcher(), new TestActorAuthorizer());

    private static TEO_V1_TravelExpenseService.SubmitInput NewInput()
        => new(Emily, "TW-TRQ-1", new[]
        {
            new TEO_V1_ExpenseItem(new DateOnly(2026, 7, 2), "Japan", "Hotel", "3 nights", "44652", "44652"),
        });

    private sealed class NullNotifyDispatcher : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static void CreateAdminTables(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL, DisplayName TEXT NOT NULL, Email TEXT NULL, Active INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL);
CREATE TABLE Admin_Roles (Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '', Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL);
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
            new SharedPrincipal { Id = Frank, Type = SharedPrincipalType.User, DisplayName = "Frank Finance",  Email = "finance@acme.tld",  Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedRoles.Add(new SharedRole { Id = FinanceRole, Code = "FINANCE", Name = "Finance", IsSystem = false });
        db.SharedPrincipalRoles.Add(new SharedPrincipalRole { PrincipalId = Frank, RoleId = FinanceRole, InheritToMembers = false, AssignedAt = now });
        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });
        db.SaveChanges();
    }
}

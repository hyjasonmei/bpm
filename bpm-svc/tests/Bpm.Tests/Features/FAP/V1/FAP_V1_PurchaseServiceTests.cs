using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.FAP.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.FAP.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.FAP.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.FAP.V1;

/// <summary>Unit tests for the FAP V1 state machine (manager approval → PO → verification).</summary>
public sealed class FAP_V1_PurchaseServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike  = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Vera  = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public FAP_V1_PurchaseServiceTests()
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
        Assert.Equal(FAP_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Single(c.PurchaseItems);
    }

    [Fact]
    public async Task Submit_without_items_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { PurchaseItems = Array.Empty<FAP_V1_PurchaseItem>() }, default));
    }

    [Fact]
    public async Task Submit_item_zero_qty_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { PurchaseItems = new[] { new FAP_V1_PurchaseItem("Hardware", "Laptop", 0) } }, default));
    }

    [Fact]
    public async Task Submit_missing_shipping_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { ShippingLocation = " " }, default));
    }

    [Fact]
    public async Task Manager_approve_issues_po_and_routes_to_verification()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(FAP_V1_CaseStatus.PendingVerification, after.Status);
        Assert.False(string.IsNullOrEmpty(after.PurchaseOrderNo));
        Assert.NotNull(after.PoIssuedAt);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);   // verification by requester
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "wrong spec", default);
        Assert.Equal(FAP_V1_CaseStatus.ResubmitRequired, after.Status);
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
    public async Task Verification_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        var done = await svc.CompleteVerificationAsync(c.Id, Emily, "Received", "all good", default);
        Assert.Equal(FAP_V1_CaseStatus.Completed, done.Status);
        Assert.Equal("Received", done.Received);
        Assert.Equal(Emily, done.VerifiedByUserId);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Verification_by_wrong_user_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        await Assert.ThrowsAsync<ForbiddenException>(async () => await svc.CompleteVerificationAsync(c.Id, Mike, "Received", null, default));
    }

    [Fact]
    public async Task Verification_before_approval_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.CompleteVerificationAsync(c.Id, Emily, "Received", null, default));
    }

    [Fact]
    public async Task Resubmit_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, NewInput() with { ChargeTo = "HQ-IT-2" }, default);
        Assert.Equal(FAP_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal("HQ-IT-2", after.ChargeTo);
    }

    [Fact]
    public async Task E2E_full_path_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var done = await svc.CompleteVerificationAsync(c.Id, Emily, "Received", "received", default);
        Assert.Equal(FAP_V1_CaseStatus.Completed, done.Status);
        Assert.False(string.IsNullOrEmpty(done.PurchaseOrderNo));
    }

    private static FAP_V1_PurchaseService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new FAP_V1_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<FAP_V1_PurchaseService>.Instance, notify ?? new NullNotifyDispatcher());

    private static FAP_V1_PurchaseService.SubmitInput NewInput()
        => new(Emily, new[] { new FAP_V1_PurchaseItem("Hardware", "Lenovo ThinkPad X250", 1) },
               "Taipei office", "TWT.1746G", "New", new DateOnly(2026, 7, 20), "for new hire");

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

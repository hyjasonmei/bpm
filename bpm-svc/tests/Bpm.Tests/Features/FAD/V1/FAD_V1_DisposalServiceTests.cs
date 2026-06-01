using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.FAD.V1;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.FAD.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.FAD.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.FAD.V1;

/// <summary>Unit tests for the FAD V1 state machine (IT judgment → confirm).</summary>
public sealed class FAD_V1_DisposalServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike  = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Vera  = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public FAD_V1_DisposalServiceTests()
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
        Assert.Equal(FAD_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal("ASSET-001", c.AssetId);
    }

    [Fact]
    public async Task Submit_missing_asset_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { AssetId = " " }, default));
    }

    [Fact]
    public async Task Submit_missing_reason_rejects()
    {
        await using var db = new AppDbContext(_options);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await NewService(db).SubmitAsync(NewInput() with { DisposalReason = "" }, default));
    }

    [Fact]
    public async Task Manager_approve_routes_to_confirm()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "判別: 失效", default);
        Assert.Equal(FAD_V1_CaseStatus.PendingConfirm, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "資產編號錯誤", default);
        Assert.Equal(FAD_V1_CaseStatus.ResubmitRequired, after.Status);
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
    public async Task Confirm_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        var done = await svc.CompleteConfirmAsync(c.Id, Emily, "失效", "已入櫃除帳", default);
        Assert.Equal(FAD_V1_CaseStatus.Completed, done.Status);
        Assert.Equal("失效", done.HandlingResult);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Confirm_without_result_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, null, default);
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.CompleteConfirmAsync(c.Id, Emily, " ", null, default));
    }

    [Fact]
    public async Task Confirm_before_approval_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.CompleteConfirmAsync(c.Id, Emily, "失效", null, default));
    }

    [Fact]
    public async Task Resubmit_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, NewInput() with { AssetId = "ASSET-002" }, default);
        Assert.Equal(FAD_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal("ASSET-002", after.AssetId);
    }

    [Fact]
    public void NotificationTemplate_Assign_renders()
    {
        var r = FAD_V1_NotificationTemplates.RenderAssign("Emily", "資產處份 — Old Laptop", "/cases/fad/x");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("/cases/fad/x", r.Body);
    }

    [Fact]
    public async Task E2E_full_path_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewInput(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var done = await svc.CompleteConfirmAsync(c.Id, Emily, "零件轉備品", "拆解後零件入庫", default);
        Assert.Equal(FAD_V1_CaseStatus.Completed, done.Status);
        Assert.Equal("零件轉備品", done.HandlingResult);
    }

    private static FAD_V1_DisposalService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new FAD_V1_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<FAD_V1_DisposalService>.Instance, notify ?? new NullNotifyDispatcher());

    private static FAD_V1_DisposalService.SubmitInput NewInput()
        => new(Emily, "老舊報廢 Obsolete", "ASSET-001", "Old Laptop", "5 years old", null);

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

using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.WFH.V5;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.WFH.V5;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.WFH.V5;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.WFH.V5;

/// <summary>
/// Unit tests for the WFH V5 state machine. Org chain: Emily → Mike →
/// Vera (top). days &lt; 90 completes at manager; days &gt;= 90 routes to
/// senior = submitter.manager.manager. (V5 raises V4's &gt;= 60 gateway to
/// &gt;= 90.) Real EF-backed store + directory + org reader against
/// in-memory SQLite.
/// </summary>
public sealed class WFH_V5_WfhServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike   = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Emily's manager
    private static readonly Guid Vera   = Guid.Parse("33333333-3333-3333-3333-333333333333"); // Mike's manager (top)
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public WFH_V5_WfhServiceTests()
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

    // ---------- Submit ----------

    [Fact]
    public async Task Submit_happy_path_routes_to_manager()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        Assert.Equal(WFH_V5_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.ManagerUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(5, c.Days); // 7/1..7/5 inclusive
    }

    [Fact]
    public async Task Submit_long_request_computes_days_over_threshold()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        Assert.Equal(100, c.Days); // 7/1..10/8 inclusive
    }

    [Fact]
    public async Task Submit_missing_reason_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(ShortRequest() with { Reason = "  " }, default));
    }

    [Fact]
    public async Task Submit_end_before_start_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await svc.SubmitAsync(ShortRequest() with { StartDate = new DateOnly(2026, 7, 5), EndDate = new DateOnly(2026, 7, 1) }, default));
    }

    [Fact]
    public async Task Submit_without_manager_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_UserManagers WHERE UserId = '11111111-1111-1111-1111-111111111111'");
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.SubmitAsync(ShortRequest(), default));
    }

    // ---------- Manager stage + gateway ----------

    [Fact]
    public async Task Manager_approve_short_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(WFH_V5_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.ManagerApproved);
        Assert.Null(done.SeniorUserId);
    }

    [Fact]
    public async Task Manager_approve_at_threshold_routes_to_senior()
    {
        // Exactly 90 days must route to senior (gateway is days >= 90).
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ThresholdRequest(), default);
        Assert.Equal(90, c.Days); // 7/1..9/28 inclusive
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(WFH_V5_CaseStatus.PendingSenior, after.Status);
        Assert.Equal(Vera, after.SeniorUserId);
    }

    [Fact]
    public async Task Manager_approve_just_below_threshold_completes()
    {
        // 89 days (one below the gate) must complete at manager.
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest() with { EndDate = new DateOnly(2026, 9, 27) }, default);
        Assert.Equal(89, c.Days);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(WFH_V5_CaseStatus.Completed, done.Status);
        Assert.Null(done.SeniorUserId);
    }

    [Fact]
    public async Task Manager_approve_long_routes_to_senior()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        var after = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(WFH_V5_CaseStatus.PendingSenior, after.Status);
        Assert.Equal(Vera, after.SeniorUserId);          // Mike.manager
        Assert.Equal(Vera, after.CurrentAssigneeUserId);
        Assert.True(after.ManagerApproved);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        var after = await svc.RejectByManagerAsync(c.Id, Mike, "請補充原因", default);
        Assert.Equal(WFH_V5_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.ManagerApproved);
    }

    [Fact]
    public async Task Manager_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByManagerAsync(c.Id, Vera, "nope", default));
    }

    // ---------- Senior stage ----------

    [Fact]
    public async Task Senior_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var done = await svc.ApproveBySeniorAsync(c.Id, Vera, "approved", default);
        Assert.Equal(WFH_V5_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.True(done.SeniorApproved);
    }

    [Fact]
    public async Task Senior_reject_returns_to_submitter()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        var after = await svc.RejectBySeniorAsync(c.Id, Vera, "天數過長", default);
        Assert.Equal(WFH_V5_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.SeniorApproved);
    }

    [Fact]
    public async Task Senior_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveBySeniorAsync(c.Id, Mike, "self", default));
    }

    [Fact]
    public async Task Senior_decision_before_manager_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ApproveBySeniorAsync(c.Id, Vera, "early", default));
    }

    // ---------- Senior resolution fallback (manager is top of chain) ----------

    [Fact]
    public async Task Senior_falls_back_to_manager_when_manager_is_top()
    {
        // Submitter = Mike (manager = Vera, Vera has no manager). days >= 90 →
        // senior = Vera.manager which is null → fall back to Vera (top level).
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var senior = await svc.ResolveSeniorApproverAsync(Mike, default);
        Assert.Equal(Vera, senior);
    }

    // ---------- Resubmit ----------

    [Fact]
    public async Task Resubmit_after_reject_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "改期", default);
        var after = await svc.ResubmitAsync(c.Id, Emily, ShortRequest() with { Reason = "改成下週" }, default);
        Assert.Equal(WFH_V5_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Null(after.ManagerApproved);
        Assert.Null(after.SeniorUserId);
        Assert.Equal("改成下週", after.Reason);
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "x", default);
        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ResubmitAsync(c.Id, Mike, ShortRequest() with { SubmitterUserId = Mike }, default));
    }

    [Fact]
    public async Task Resubmit_when_not_in_resubmit_state_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ResubmitAsync(c.Id, Emily, ShortRequest(), default));
    }

    // ---------- Withdraw ----------

    [Fact]
    public async Task Submitter_withdraws_pending_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        var after = await svc.CancelAsync(c.Id, Emily, default);
        Assert.Equal(WFH_V5_CaseStatus.Cancelled, after.Status);
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.NotNull(after.CompletedAt);
    }

    [Fact]
    public async Task Withdraw_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await Assert.ThrowsAsync<ForbiddenException>(async () => await svc.CancelAsync(c.Id, Mike, default));
    }

    [Fact]
    public async Task Withdraw_completed_case_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        await Assert.ThrowsAsync<ConflictException>(async () => await svc.CancelAsync(c.Id, Emily, default));
    }

    // ---------- Pure helpers ----------

    [Fact]
    public void ComputeConsecutiveDays_is_inclusive()
    {
        Assert.Equal(1, WFH_V5_WfhService.ComputeConsecutiveDays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1)));
        Assert.Equal(30, WFH_V5_WfhService.ComputeConsecutiveDays(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 30)));
        Assert.Equal(0, WFH_V5_WfhService.ComputeConsecutiveDays(new DateOnly(2026, 7, 8), new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void NotificationTemplate_Assign_renders_summary()
    {
        var r = WFH_V5_NotificationTemplates.RenderAssign("Emily", "居家辦公 — 90 天（2026/07/01 ~ 2026/09/28）", "/cases/wfh/x");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("居家辦公", r.Body);
        Assert.Contains("/cases/wfh/x", r.Body);
    }

    [Fact]
    public void NotificationTemplate_Reject_renders_reason()
    {
        var r = WFH_V5_NotificationTemplates.RenderReject("天數過長", "/cases/wfh/x");
        Assert.Contains("被駁回", r.Subject);
        Assert.Contains("天數過長", r.Body);
    }

    // ---------- End-to-end (every node on the long branch) ----------

    [Fact]
    public async Task E2E_long_request_manager_then_senior_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(LongRequest(), default);
        Assert.Equal(WFH_V5_CaseStatus.PendingManager, c.Status);
        var afterMgr = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);
        Assert.Equal(WFH_V5_CaseStatus.PendingSenior, afterMgr.Status);
        var done = await svc.ApproveBySeniorAsync(c.Id, Vera, "approved", default);
        Assert.Equal(WFH_V5_CaseStatus.Completed, done.Status);
        Assert.True(done.ManagerApproved);
        Assert.True(done.SeniorApproved);
    }

    [Fact]
    public async Task E2E_reject_resubmit_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(ShortRequest(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "改期再送", default);
        await svc.ResubmitAsync(c.Id, Emily, ShortRequest() with { Reason = "已改期" }, default);
        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok now", default);
        Assert.Equal(WFH_V5_CaseStatus.Completed, done.Status);
        Assert.Equal(2, done.RoundCount);
        Assert.Equal("已改期", done.Reason);
    }

    // ---------- Fixtures ----------

    private static WFH_V5_WfhService NewService(AppDbContext db, INotifyDispatcher? notify = null)
        => new(new WFH_V5_CaseStore(db), new OrgChartReader(db), new PrincipalDirectory(db),
               new StubClock(), NullLogger<WFH_V5_WfhService>.Instance, notify ?? new NullNotifyDispatcher(), new TestActorAuthorizer());

    // Short = 5 days (< 90) → completes at manager.
    private static WFH_V5_WfhService.SubmitInput ShortRequest()
        => new(Emily, new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5), "家中有事需居家辦公", null);

    // Threshold = exactly 90 days → routes to senior.
    private static WFH_V5_WfhService.SubmitInput ThresholdRequest()
        => new(Emily, new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 28), "需連續三個月居家辦公", null);

    // Long = 100 days (>= 90) → routes to senior.
    private static WFH_V5_WfhService.SubmitInput LongRequest()
        => new(Emily, new DateOnly(2026, 6, 20), new DateOnly(2026, 7, 1), new DateOnly(2026, 10, 8), "長期居家辦公需求", null);

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
        // Org chain: Emily → Mike → Vera (top).
        db.SharedUserManagers.AddRange(
            new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now },
            new SharedUserManager { UserId = Mike,  ManagerUserId = Vera, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });
        db.SaveChanges();
    }
}

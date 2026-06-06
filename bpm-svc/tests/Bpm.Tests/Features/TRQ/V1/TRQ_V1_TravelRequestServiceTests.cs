using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.TRQ.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.TRQ.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.TRQ.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.TRQ.V1;

/// <summary>
/// Unit tests for the TRQ V1 state machine. Wires the real EF-backed
/// CaseStore + PrincipalDirectory + OrgChartReader against an in-memory
/// SQLite so the pass exercises the same Application↔Persistence seam
/// the production wiring uses. Approver = submitter.manager (Mike).
/// </summary>
public sealed class TRQ_V1_TravelRequestServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily  = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike   = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Emily's manager
    private static readonly Guid Vera   = Guid.Parse("33333333-3333-3333-3333-333333333333"); // not the manager
    private static readonly Guid HqDept = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public TRQ_V1_TravelRequestServiceTests()
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
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        Assert.Equal(TRQ_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.ManagerUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(1, c.RoundCount);
        Assert.Equal(Emily, c.SubmitterUserId);
        Assert.Equal("Taipei", c.DepartureCity);
    }

    [Fact]
    public async Task Submit_missing_destination_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = NewSubmitInput() with { DestinationCity = "  " };
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_missing_depart_date_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = NewSubmitInput() with { DepartDate = default };
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_missing_purpose_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = NewSubmitInput() with { TravelPurpose = "" };
        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_without_manager_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_UserManagers");
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.SubmitAsync(NewSubmitInput(), default));
    }

    [Fact]
    public async Task Submit_when_manager_is_submitter_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE Admin_UserManagers SET ManagerUserId = {0} WHERE UserId = {1}", Emily, Emily);
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.SubmitAsync(NewSubmitInput(), default));
    }

    // ============================================================
    // Manager decision
    // ============================================================

    [Fact]
    public async Task Manager_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var done = await svc.ApproveByManagerAsync(c.Id, Mike, "ok", default);

        Assert.Equal(TRQ_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.ManagerApproved);
        Assert.Equal("ok", done.ManagerComment);
    }

    [Fact]
    public async Task Manager_reject_returns_to_submitter_for_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var after = await svc.RejectByManagerAsync(c.Id, Mike, "wrong dates", default);

        Assert.Equal(TRQ_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.ManagerApproved);
        Assert.Equal("wrong dates", after.ManagerComment);
    }

    [Fact]
    public async Task Manager_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByManagerAsync(c.Id, Vera, "not the manager", default));
    }

    [Fact]
    public async Task Decision_on_unknown_case_is_not_found()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<NotFoundException>(async () =>
            await svc.ApproveByManagerAsync(Guid.NewGuid(), Mike, null, default));
    }

    // ============================================================
    // Resubmit loop
    // ============================================================

    [Fact]
    public async Task Resubmit_after_reject_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "fix return date", default);

        var resubmit = NewSubmitInput() with
        {
            ReturnDate = new DateOnly(2026, 7, 10),
            TravelPurpose = "Client kickoff (revised)",
        };
        var after = await svc.ResubmitAsync(c.Id, Emily, resubmit, default);

        Assert.Equal(TRQ_V1_CaseStatus.PendingManager, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal(Mike, after.CurrentAssigneeUserId);
        Assert.Null(after.ManagerApproved);   // cleared
        Assert.Null(after.ManagerComment);
        Assert.Equal(new DateOnly(2026, 7, 10), after.ReturnDate);
        Assert.Equal("Client kickoff (revised)", after.TravelPurpose);
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "fix", default);

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

    // ============================================================
    // Notification templates
    // ============================================================

    [Fact]
    public void NotificationTemplate_Submitted_renders_caseUrl()
    {
        var r = TRQ_V1_NotificationTemplates.RenderSubmitted(caseUrl: "/cases/trq/abc");
        Assert.Contains("已收到", r.Subject);
        Assert.Contains("/cases/trq/abc", r.Body);
    }

    [Fact]
    public void NotificationTemplate_Assign_renders_applicant_and_summary()
    {
        var r = TRQ_V1_NotificationTemplates.RenderAssign(
            applicantName: "Emily",
            summary: "差旅申請 — Taipei → Tokyo（2026-07-01）",
            caseUrl: "/cases/trq/xyz");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("Taipei → Tokyo", r.Body);
        Assert.Contains("/cases/trq/xyz", r.Body);
    }

    // ============================================================
    // E2E
    // ============================================================

    [Fact]
    public async Task E2E_happy_path_submit_then_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        var completed = await svc.ApproveByManagerAsync(c.Id, Mike, "approved", default);

        Assert.Equal(TRQ_V1_CaseStatus.Completed, completed.Status);
        Assert.NotNull(completed.ManagerDecisionAt);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(Mike, completed.ManagerUserId);
    }

    [Fact]
    public async Task E2E_reject_resubmit_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByManagerAsync(c.Id, Mike, "wrong city", default);
        await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput() with { DestinationCity = "Osaka" }, default);
        var completed = await svc.ApproveByManagerAsync(c.Id, Mike, "ok now", default);

        Assert.Equal(TRQ_V1_CaseStatus.Completed, completed.Status);
        Assert.Equal(2, completed.RoundCount);
        Assert.Equal("Osaka", completed.DestinationCity);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static TRQ_V1_TravelRequestService NewService(AppDbContext db, INotifyDispatcher? notify = null)
    {
        ITRQ_V1_CaseStore store = new TRQ_V1_CaseStore(db);
        IOrgChartReader org = new OrgChartReader(db);
        IPrincipalDirectory directory = new PrincipalDirectory(db);
        return new TRQ_V1_TravelRequestService(
            store, org, directory, new StubClock(),
            NullLogger<TRQ_V1_TravelRequestService>.Instance,
            notify ?? new NullNotifyDispatcher(), new TestActorAuthorizer());
    }

    private static TRQ_V1_TravelRequestService.SubmitInput NewSubmitInput()
        => new(
            SubmitterUserId: Emily,
            TravelType: "Round Trip",
            DepartureCity: "Taipei",
            DestinationCity: "Tokyo",
            DepartDate: new DateOnly(2026, 7, 1),
            ReturnDate: new DateOnly(2026, 7, 5),
            ChargeTo: "HQ-IT",
            TravelPurpose: "Client kickoff",
            PassportName: "Emily Employee",
            SeatPreference: "Window",
            PickupRequired: "No");

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
            new SharedPrincipal { Id = Emily, Type = SharedPrincipalType.User, DisplayName = "Emily Employee", Email = "employee@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Mike,  Type = SharedPrincipalType.User, DisplayName = "Mike Manager",   Email = "manager@acme.tld",  Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Vera,  Type = SharedPrincipalType.User, DisplayName = "Vera VP",        Email = "vp@acme.tld",       Active = true, CreatedAt = now, UpdatedAt = now });

        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });

        db.SaveChanges();
    }
}

using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.PURCHASE_REQUEST.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Unit tests for the PURCHASE_REQUEST V1 state machine. Wires the
/// real EF-backed CaseStore + PrincipalDirectory + OrgChartReader
/// against an in-memory SQLite so the test pass exercises the same
/// Application↔Persistence seam the production wiring uses.
/// </summary>
public sealed class PURCHASE_REQUEST_V1_PurchaseRequestServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    // Seed actors — semantic matches for the bundle's sample-org.
    private static readonly Guid Emily       = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike        = Guid.Parse("22222222-2222-2222-2222-222222222222"); // dept head
    private static readonly Guid Frank       = Guid.Parse("00000000-0000-0000-0000-00000000f4a1"); // Finance role
    private static readonly Guid HqDept      = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FinanceRole = Guid.Parse("c3a098d8-d377-4f45-a1bb-0cc23386a7c1"); // bundle's literal id

    public PURCHASE_REQUEST_V1_PurchaseRequestServiceTests()
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
    public async Task Submit_happy_path_routes_to_dept_head()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead, c.Status);
        Assert.Equal(Mike, c.DeptHeadUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(1, c.RoundCount);
        Assert.Equal(Emily, c.SubmitterUserId);
        Assert.Single(c.Invoices);
    }

    [Fact]
    public async Task Submit_without_invoices_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = new PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput(
            SubmitterUserId: Emily,
            SubmitterComment: "ok",
            Invoices: Array.Empty<PURCHASE_REQUEST_V1_Invoice>());

        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_without_comment_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = new PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput(
            SubmitterUserId: Emily,
            SubmitterComment: "   ",
            Invoices: new[] { OneInvoice() });

        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_with_invoice_missing_date_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var bad = new PURCHASE_REQUEST_V1_Invoice(
            InvoiceDate: default, InvoiceNo: "X-1", ChargeTo: null, Project: null,
            Category: null, Amount: null, Currency: null, AttachmentFileId: null, Description: null);
        var input = new PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput(
            SubmitterUserId: Emily, SubmitterComment: "ok", Invoices: new[] { bad });

        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_without_dept_head_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_DeptHeads");
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.SubmitAsync(NewSubmitInput(), default));
    }

    [Fact]
    public async Task Submit_when_head_is_submitter_conflicts()
    {
        // Promote Emily as her own dept head — should refuse to route.
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE Admin_DeptHeads SET HeadUserId = {0} WHERE DeptId = {1}", Emily, HqDept);
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.SubmitAsync(NewSubmitInput(), default));
    }

    // ============================================================
    // Dept-head decision
    // ============================================================

    [Fact]
    public async Task DeptHead_approve_routes_to_finance()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var after = await svc.ApproveByDeptHeadAsync(c.Id, Mike, "ok", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.PendingFinance, after.Status);
        Assert.Null(after.FinanceUserId);                      // shared role queue — no single designated user
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.Equal("FINANCE", after.CurrentAssigneeRoleCode);
        Assert.True(after.DeptHeadApproved);
    }

    [Fact]
    public async Task DeptHead_reject_returns_to_submitter_for_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var after = await svc.RejectByDeptHeadAsync(c.Id, Mike, "missing receipt", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.DeptHeadApproved);
        Assert.Equal("missing receipt", after.DeptHeadComment);
    }

    [Fact]
    public async Task DeptHead_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByDeptHeadAsync(c.Id, Frank, "I'm not Mike", default));
    }

    [Fact]
    public async Task DeptHead_approve_without_finance_role_member_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_PrincipalRoles");
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ApproveByDeptHeadAsync(c.Id, Mike, null, default));
    }

    // ============================================================
    // Finance decision
    // ============================================================

    [Fact]
    public async Task Finance_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveByDeptHeadAsync(c.Id, Mike, null, default);

        var done = await svc.ApproveByFinanceAsync(c.Id, Frank, "approved", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.FinanceApproved);
    }

    [Fact]
    public async Task Finance_reject_returns_to_submitter_for_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveByDeptHeadAsync(c.Id, Mike, null, default);

        var after = await svc.RejectByFinanceAsync(c.Id, Frank, "amount mismatch", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.FinanceApproved);
    }

    [Fact]
    public async Task Finance_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveByDeptHeadAsync(c.Id, Mike, null, default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByFinanceAsync(c.Id, Mike, null, default));
    }

    // ============================================================
    // Resubmit loop
    // ============================================================

    [Fact]
    public async Task Resubmit_after_dept_head_reject_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByDeptHeadAsync(c.Id, Mike, "fix the project code", default);

        var fixedInvoice = new PURCHASE_REQUEST_V1_Invoice(
            InvoiceDate: new DateOnly(2026, 5, 15), InvoiceNo: "INV-002",
            ChargeTo: "HQ-IT", Project: "ACME-2026", Category: "Software", Amount: "1200",
            Currency: "USD", AttachmentFileId: null, Description: "Re-issued invoice");
        var resubmitInput = new PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput(
            SubmitterUserId: Emily, SubmitterComment: "Updated invoice + project code",
            Invoices: new[] { fixedInvoice });

        var after = await svc.ResubmitAsync(c.Id, Emily, resubmitInput, default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal(Mike, after.CurrentAssigneeUserId);
        Assert.Null(after.DeptHeadApproved);   // cleared
        Assert.Null(after.DeptHeadComment);
        Assert.Equal("Updated invoice + project code", after.SubmitterComment);
        Assert.Equal("INV-002", after.Invoices.Single().InvoiceNo);
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByDeptHeadAsync(c.Id, Mike, "fix", default);

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
        var r = PURCHASE_REQUEST_V1_NotificationTemplates.RenderSubmitted(caseUrl: "/cases/purchase-request/abc");
        Assert.Contains("已收到", r.Subject);
        Assert.Contains("/cases/purchase-request/abc", r.Body);
    }

    [Fact]
    public void NotificationTemplate_Assign_renders_applicant_and_summary()
    {
        var r = PURCHASE_REQUEST_V1_NotificationTemplates.RenderAssign(
            applicantName: "Emily",
            summary: "採購申請 — 2 invoices",
            caseUrl: "/cases/purchase-request/xyz");
        Assert.Contains("Emily", r.Subject);
        Assert.Contains("採購申請 — 2 invoices", r.Body);
        Assert.Contains("/cases/purchase-request/xyz", r.Body);
    }

    // ============================================================
    // E2E happy path
    // ============================================================

    [Fact]
    public async Task E2E_happy_path_submit_dept_finance_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        var atFinance = await svc.ApproveByDeptHeadAsync(c.Id, Mike, "ok", default);
        var completed = await svc.ApproveByFinanceAsync(c.Id, Frank, "approved", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.Completed, completed.Status);
        Assert.NotNull(completed.DeptHeadDecisionAt);
        Assert.NotNull(completed.FinanceDecisionAt);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(Mike, completed.DeptHeadUserId);
        Assert.Equal(Frank, completed.FinanceUserId);
    }

    [Fact]
    public async Task E2E_reject_resubmit_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectByDeptHeadAsync(c.Id, Mike, "wrong category", default);
        await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput() with { SubmitterComment = "Fixed category" }, default);
        await svc.ApproveByDeptHeadAsync(c.Id, Mike, "ok now", default);
        var completed = await svc.ApproveByFinanceAsync(c.Id, Frank, "approved", default);

        Assert.Equal(PURCHASE_REQUEST_V1_CaseStatus.Completed, completed.Status);
        Assert.Equal(2, completed.RoundCount);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static PURCHASE_REQUEST_V1_PurchaseRequestService NewService(
        AppDbContext db, INotifyDispatcher? notify = null)
    {
        IPURCHASE_REQUEST_V1_CaseStore store = new PURCHASE_REQUEST_V1_CaseStore(db);
        IOrgChartReader org = new OrgChartReader(db);
        IPrincipalDirectory directory = new PrincipalDirectory(db);
        return new PURCHASE_REQUEST_V1_PurchaseRequestService(
            store, org, directory, new StubClock(),
            NullLogger<PURCHASE_REQUEST_V1_PurchaseRequestService>.Instance,
            notify ?? new NullNotifyDispatcher(),
            new TestActorAuthorizer(new Dictionary<Guid, IReadOnlySet<string>>
            {
                [Frank] = new HashSet<string> { "FINANCE" },
            }));
    }

    private static PURCHASE_REQUEST_V1_Invoice OneInvoice()
        => new(
            InvoiceDate:      new DateOnly(2026, 5, 12),
            InvoiceNo:        "INV-001",
            ChargeTo:         "HQ-IT",
            Project:          "ACME-2026",
            Category:         "Software",
            Amount:           "1000",
            Currency:         "USD",
            AttachmentFileId: null,
            Description:      "Sample purchase line");

    private static PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput NewSubmitInput()
        => new(
            SubmitterUserId: Emily,
            SubmitterComment: "Please approve this PR for Q2 supplier invoices.",
            Invoices: new[] { OneInvoice() });

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
            new SharedPrincipal { Id = Frank, Type = SharedPrincipalType.User, DisplayName = "Frank Finance",  Email = "finance@acme.tld",  Active = true, CreatedAt = now, UpdatedAt = now });

        db.SharedRoles.Add(new SharedRole { Id = FinanceRole, Code = "FINANCE", Name = "Finance", IsSystem = false });

        db.SharedPrincipalRoles.Add(new SharedPrincipalRole
        {
            PrincipalId = Frank, RoleId = FinanceRole, InheritToMembers = false, AssignedAt = now,
        });

        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });

        db.SaveChanges();
    }
}

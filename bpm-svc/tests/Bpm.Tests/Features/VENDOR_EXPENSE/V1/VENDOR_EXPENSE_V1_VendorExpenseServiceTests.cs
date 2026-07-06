using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.VENDOR_EXPENSE.V1;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Unit tests for the VENDOR_EXPENSE V1 state machine. Wires the real
/// EF-backed CaseStore + PrincipalDirectory + OrgChartReader against an
/// in-memory SQLite so the pass exercises the same Application↔Persistence
/// seam the production wiring uses.
///
/// Spec ⇄ sampleOrg drift: the bundle's sample-org assigns only HR / VP
/// roles, so there is no member of the procurement role. Tests seed a
/// "Procurement" role + a dedicated member (Pam) to exercise the
/// procurement stage — production resolves the same role by name.
/// </summary>
public sealed class VENDOR_EXPENSE_V1_VendorExpenseServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    // Seed actors — semantic matches for the bundle's sample-org.
    private static readonly Guid Emily          = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike           = Guid.Parse("22222222-2222-2222-2222-222222222222"); // dept head — supervisor + signer
    private static readonly Guid Pam            = Guid.Parse("00000000-0000-0000-0000-0000000000a1"); // Procurement role member
    private static readonly Guid HqDept         = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ProcurementRole = Guid.Parse("c3a098d8-d377-4f45-a1bb-0cc23386a7c1"); // bundle's literal role id

    public VENDOR_EXPENSE_V1_VendorExpenseServiceTests()
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
    public async Task Submit_happy_path_routes_to_supervisor()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor, c.Status);
        Assert.Equal(Mike, c.SupervisorUserId);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);
        Assert.Equal(1, c.RoundCount);
        Assert.Equal(Emily, c.SubmitterUserId);
        Assert.Equal("Acme Supplies Inc.", c.Vendor);
        Assert.Single(c.Invoices);
    }

    [Fact]
    public async Task Submit_without_invoices_rejects()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = new VENDOR_EXPENSE_V1_VendorExpenseService.SubmitInput(
            SubmitterUserId: Emily, Vendor: "X", SubmitterComment: "ok",
            Invoices: Array.Empty<VENDOR_EXPENSE_V1_Invoice>());

        await Assert.ThrowsAsync<ValidationException>(async () => await svc.SubmitAsync(input, default));
    }

    [Fact]
    public async Task Submit_without_comment_is_allowed()
    {
        // promptComment is "optional" in the spec — a blank comment must not block submit.
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var input = new VENDOR_EXPENSE_V1_VendorExpenseService.SubmitInput(
            SubmitterUserId: Emily, Vendor: null, SubmitterComment: null,
            Invoices: new[] { OneInvoice() });

        var c = await svc.SubmitAsync(input, default);
        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor, c.Status);
        Assert.Null(c.SubmitterComment);
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
    // Supervisor decision (主管審核)
    // ============================================================

    [Fact]
    public async Task Supervisor_approve_routes_to_procurement()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var after = await svc.ApproveBySupervisorAsync(c.Id, Mike, "ok", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.PendingProcurement, after.Status);
        Assert.Null(after.ProcurementUserId);                  // shared role queue — no single designated user
        Assert.Null(after.CurrentAssigneeUserId);
        Assert.Equal("PROCUREMENT", after.CurrentAssigneeRoleCode);
        Assert.True(after.SupervisorApproved);
    }

    [Fact]
    public async Task Supervisor_reject_returns_to_submitter_for_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        var after = await svc.RejectBySupervisorAsync(c.Id, Mike, "missing PO", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.SupervisorApproved);
        Assert.Equal("missing PO", after.SupervisorComment);
    }

    [Fact]
    public async Task Supervisor_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveBySupervisorAsync(c.Id, Pam, "not the supervisor", default));
    }

    [Fact]
    public async Task Supervisor_approve_without_procurement_member_conflicts()
    {
        await using (var seed = new AppDbContext(_options))
        {
            await seed.Database.ExecuteSqlRawAsync("DELETE FROM Admin_PrincipalRoles");
        }
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);

        await Assert.ThrowsAsync<ConflictException>(async () =>
            await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default));
    }

    // ============================================================
    // Procurement decision (採購審定)
    // ============================================================

    [Fact]
    public async Task Procurement_approve_routes_to_sign()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);

        var after = await svc.ApproveByProcurementAsync(c.Id, Pam, "vendor vetted", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.PendingSign, after.Status);
        Assert.Equal(Mike, after.SignUserId);            // dept head signs (same ActorRef as supervisor)
        Assert.Equal(Mike, after.CurrentAssigneeUserId);
        Assert.True(after.ProcurementApproved);
    }

    [Fact]
    public async Task Procurement_reject_returns_to_submitter_for_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);

        var after = await svc.RejectByProcurementAsync(c.Id, Pam, "wrong vendor", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.ProcurementApproved);
    }

    [Fact]
    public async Task Procurement_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveByProcurementAsync(c.Id, Mike, "not procurement", default));
    }

    // ============================================================
    // Sign decision (簽核)
    // ============================================================

    [Fact]
    public async Task Sign_approve_completes_case()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);
        await svc.ApproveByProcurementAsync(c.Id, Pam, null, default);

        var done = await svc.ApproveBySignAsync(c.Id, Mike, "signed", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.Completed, done.Status);
        Assert.Null(done.CurrentAssigneeUserId);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.SignApproved);
    }

    [Fact]
    public async Task Sign_reject_returns_to_submitter_for_resubmit()
    {
        // Baked decision: approval_sign has no reject edge in the BPMN, but
        // its reject button + on_reject template route back to the submitter.
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);
        await svc.ApproveByProcurementAsync(c.Id, Pam, null, default);

        var after = await svc.RejectBySignAsync(c.Id, Mike, "need re-sign", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.Equal(Emily, after.CurrentAssigneeUserId);
        Assert.False(after.SignApproved);
    }

    [Fact]
    public async Task Sign_decision_by_wrong_user_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, null, default);
        await svc.ApproveByProcurementAsync(c.Id, Pam, null, default);

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
            await svc.ApproveBySignAsync(c.Id, Pam, "not the signer", default));
    }

    // ============================================================
    // Resubmit loop
    // ============================================================

    [Fact]
    public async Task Resubmit_after_reject_clears_stages_and_starts_round_2()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, "ok", default);
        await svc.RejectByProcurementAsync(c.Id, Pam, "fix vendor code", default);

        var resubmitInput = new VENDOR_EXPENSE_V1_VendorExpenseService.SubmitInput(
            SubmitterUserId: Emily, Vendor: "Acme Supplies Inc. (rev)", SubmitterComment: "Fixed vendor code",
            Invoices: new[] { OneInvoice() with { InvoiceNo = "INV-002" } });

        var after = await svc.ResubmitAsync(c.Id, Emily, resubmitInput, default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor, after.Status);
        Assert.Equal(2, after.RoundCount);
        Assert.Equal(Mike, after.CurrentAssigneeUserId);
        Assert.Null(after.SupervisorApproved);    // cleared
        Assert.Null(after.ProcurementApproved);    // cleared
        Assert.Null(after.ProcurementComment);
        Assert.Equal("Fixed vendor code", after.SubmitterComment);
        Assert.Equal("INV-002", after.Invoices.Single().InvoiceNo);
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectBySupervisorAsync(c.Id, Mike, "fix", default);

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
        var r = VENDOR_EXPENSE_V1_NotificationTemplates.RenderSubmitted(caseUrl: "/cases/vendor_expense/abc");
        Assert.Contains("已收到", r.Subject);
        Assert.Contains("/cases/vendor_expense/abc", r.Body);
    }

    [Fact]
    public void NotificationTemplate_Rejected_renders_reason_and_caseUrl()
    {
        var r = VENDOR_EXPENSE_V1_NotificationTemplates.RenderRejected(
            rejectReason: "金額不符", caseUrl: "/cases/vendor_expense/xyz");
        Assert.Contains("被駁回", r.Subject);
        Assert.Contains("金額不符", r.Body);
        Assert.Contains("/cases/vendor_expense/xyz", r.Body);
    }

    // ============================================================
    // E2E (every node of the spec)
    // ============================================================

    [Fact]
    public async Task E2E_happy_path_supervisor_procurement_sign_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        var atProcurement = await svc.ApproveBySupervisorAsync(c.Id, Mike, "ok", default);
        var atSign = await svc.ApproveByProcurementAsync(atProcurement.Id, Pam, "vetted", default);
        var completed = await svc.ApproveBySignAsync(atSign.Id, Mike, "signed", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.Completed, completed.Status);
        Assert.NotNull(completed.SupervisorDecisionAt);
        Assert.NotNull(completed.ProcurementDecisionAt);
        Assert.NotNull(completed.SignDecisionAt);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(Mike, completed.SupervisorUserId);
        Assert.Equal(Pam, completed.ProcurementUserId);
        Assert.Equal(Mike, completed.SignUserId);
    }

    [Fact]
    public async Task E2E_reject_resubmit_then_full_approve_completes()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var c = await svc.SubmitAsync(NewSubmitInput(), default);
        await svc.RejectBySupervisorAsync(c.Id, Mike, "wrong category", default);
        await svc.ResubmitAsync(c.Id, Emily, NewSubmitInput() with { SubmitterComment = "Fixed category" }, default);
        await svc.ApproveBySupervisorAsync(c.Id, Mike, "ok now", default);
        await svc.ApproveByProcurementAsync(c.Id, Pam, "vetted", default);
        var completed = await svc.ApproveBySignAsync(c.Id, Mike, "signed", default);

        Assert.Equal(VENDOR_EXPENSE_V1_CaseStatus.Completed, completed.Status);
        Assert.Equal(2, completed.RoundCount);
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static VENDOR_EXPENSE_V1_VendorExpenseService NewService(
        AppDbContext db, INotifyDispatcher? notify = null)
    {
        IVENDOR_EXPENSE_V1_CaseStore store = new VENDOR_EXPENSE_V1_CaseStore(db);
        IOrgChartReader org = new OrgChartReader(db);
        Bpm.Application.Common.Directory.IPrincipalDirectory directory = new PrincipalDirectory(db);
        return new VENDOR_EXPENSE_V1_VendorExpenseService(
            store, org, directory, new StubClock(),
            NullLogger<VENDOR_EXPENSE_V1_VendorExpenseService>.Instance,
            notify ?? new NullNotifyDispatcher(),
            new TestActorAuthorizer(new Dictionary<Guid, IReadOnlySet<string>>
            {
                [Pam] = new HashSet<string> { "PROCUREMENT" },
            }));
    }

    private static VENDOR_EXPENSE_V1_Invoice OneInvoice()
        => new(
            InvoiceDate: new DateOnly(2026, 5, 12),
            InvoiceNo:   "INV-001",
            ChargeTo:    "HQ-IT",
            Project:     "ACME-2026",
            Category:    "Software",
            Amount:      "1000",
            Currency:    "USD",
            Description: "Sample vendor expense line");

    private static VENDOR_EXPENSE_V1_VendorExpenseService.SubmitInput NewSubmitInput()
        => new(
            SubmitterUserId: Emily,
            Vendor: "Acme Supplies Inc.",
            SubmitterComment: "Q2 supplier invoices for the Acme integration project.",
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
            new SharedPrincipal { Id = Emily, Type = SharedPrincipalType.User, DisplayName = "Emily Employee",   Email = "employee@acme.tld",    Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Mike,  Type = SharedPrincipalType.User, DisplayName = "Mike Manager",     Email = "manager@acme.tld",     Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Pam,   Type = SharedPrincipalType.User, DisplayName = "Pam Procurement",  Email = "procurement@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now });

        db.SharedRoles.Add(new SharedRole { Id = ProcurementRole, Code = "PROCUREMENT", Name = "Procurement", IsSystem = false });

        db.SharedPrincipalRoles.Add(new SharedPrincipalRole
        {
            PrincipalId = Pam, RoleId = ProcurementRole, InheritToMembers = false, AssignedAt = now,
        });

        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SharedUserDepts.Add(new SharedUserDept { UserId = Emily, DeptId = HqDept, IsPrimary = true });
        db.SharedDeptHeads.Add(new SharedDeptHead { DeptId = HqDept, HeadUserId = Mike, AssignedAt = now });

        db.SaveChanges();
    }
}

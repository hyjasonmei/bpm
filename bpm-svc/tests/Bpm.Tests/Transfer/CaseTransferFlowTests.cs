using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.OVERTIME.V1;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.OVERTIME.V1;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Features.OVERTIME.V1;
using Bpm.Persistence.Org;
using Bpm.Persistence.SharedIdentity;
using Bpm.Persistence.Transfer;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Transfer;

/// <summary>
/// End-to-end transfer (轉簽) through a REAL flow state machine: OVERTIME
/// submit → manager transfers to a colleague → the OLD assignee is denied,
/// the NEW assignee's decision drives the case forward. Proves the
/// CurrentAssignee* guard convention and the transfer primitive compose.
/// </summary>
public sealed class CaseTransferFlowTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Emily = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mike  = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Emily's manager
    private static readonly Guid Carol = Guid.Parse("33333333-3333-3333-3333-333333333333"); // transfer target

    public CaseTransferFlowTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (
    Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL,
    DisplayName TEXT NOT NULL, Email TEXT NULL, Active INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL
);
CREATE TABLE Admin_Roles (
    Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '',
    Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL
);
CREATE TABLE Admin_PrincipalRoles (
    PrincipalId TEXT NOT NULL, RoleId TEXT NOT NULL,
    InheritToMembers INTEGER NOT NULL, IncludeSubDepts INTEGER NOT NULL DEFAULT 0,
    AssignedAt TEXT NOT NULL, AssignedByUserId TEXT NULL,
    PRIMARY KEY (PrincipalId, RoleId)
);
CREATE TABLE Admin_DeptParents (DeptId TEXT NOT NULL PRIMARY KEY, ParentDeptId TEXT NULL);
CREATE TABLE Admin_UserManagers (
    UserId TEXT NOT NULL PRIMARY KEY, ManagerUserId TEXT NOT NULL, AssignedAt TEXT NOT NULL
);
CREATE TABLE Admin_UserDepts (
    UserId TEXT NOT NULL, DeptId TEXT NOT NULL, IsPrimary INTEGER NOT NULL,
    PRIMARY KEY (UserId, DeptId)
);
CREATE TABLE Admin_DeptHeads (
    DeptId TEXT NOT NULL PRIMARY KEY, HeadUserId TEXT NOT NULL, AssignedAt TEXT NOT NULL
);");
        var now = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        db.SharedPrincipals.AddRange(
            new SharedPrincipal { Id = Emily, Type = SharedPrincipalType.User, DisplayName = "Emily", Email = "e@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Mike,  Type = SharedPrincipalType.User, DisplayName = "Mike",  Email = "m@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Carol, Type = SharedPrincipalType.User, DisplayName = "Carol", Email = "c@acme.tld", Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedUserManagers.Add(new SharedUserManager { UserId = Emily, ManagerUserId = Mike, AssignedAt = now });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private static OVERTIME_V1_OvertimeService NewFlowService(AppDbContext db)
    {
        IOVERTIME_V1_CaseStore store = new OVERTIME_V1_CaseStore(db);
        IOrgChartReader org = new OrgChartReader(db);
        IPrincipalDirectory directory = new PrincipalDirectory(db);
        return new OVERTIME_V1_OvertimeService(
            store, org, directory, new StubClock(),
            NullLogger<OVERTIME_V1_OvertimeService>.Instance,
            new NullNotify(),
            new TestActorAuthorizer());
    }

    private sealed class NullNotify : INotifyDispatcher
    {
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Manager_transfers_then_new_assignee_approves_and_old_cannot()
    {
        await using var db = new AppDbContext(_options);
        var flow = NewFlowService(db);

        // 1. Emily submits → PendingManager, assignee = Mike
        var c = await flow.SubmitAsync(new OVERTIME_V1_OvertimeService.SubmitInput(
            SubmitterUserId: Emily,
            OvertimeDate: new DateOnly(2026, 7, 10),
            StartTime: "18:00", EndTime: "21:00",
            EstimatedHours: 3m, OvertimeReason: "趕上線"), default);
        Assert.Equal(OVERTIME_V1_CaseStatus.PendingManager, c.Status);
        Assert.Equal(Mike, c.CurrentAssigneeUserId);

        // 2. Mike transfers to Carol
        var transfer = new CaseTransferService(db, new TestActorAuthorizer(), new StubClock(), new NullNotify(), NullLogger<CaseTransferService>.Instance);
        var r = await transfer.TransferAsync("OVERTIME", c.Id, Mike, Carol, "出差請 Carol 代審", default);
        Assert.True(r.Ok);

        // 3. Old assignee can no longer act
        await Assert.ThrowsAsync<ForbiddenException>(
            () => flow.ApproveByManagerAsync(c.Id, Mike, "should fail", default));

        // 4. New assignee's decision drives the case forward. 3h is under the
        //    monthly HR gate and this is the only case → deterministically Completed.
        var done = await flow.ApproveByManagerAsync(c.Id, Carol, "OK", default);
        Assert.Equal(OVERTIME_V1_CaseStatus.Completed, done.Status);
        Assert.True(done.ManagerApproved);

        // 5. Exactly one audit row
        var log = await db.CaseTransferLogs.SingleAsync();
        Assert.Equal(Mike, log.FromUserId);
        Assert.Equal(Carol, log.ToUserId);
        Assert.Equal(c.Id, log.CaseId);
    }
}

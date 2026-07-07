using Bpm.Application.Attendance;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Domain.Entities.Attendance;
using Bpm.Persistence;
using Bpm.Persistence.Attendance;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.SharedIdentity;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Attendance;

/// <summary>
/// 補打卡 real flow: submit (validations + duplicate guard) → direct manager
/// decides (authz) → approval inserts the missing punch with Source=Correction.
/// </summary>
public sealed class AttendanceCorrectionServiceTests : IDisposable
{
    private static readonly Guid Bob   = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    private static readonly Guid Alice = Guid.Parse("00000000-0000-0000-0000-00000000a11c");
    private static readonly Guid Erin  = Guid.Parse("00000000-0000-0000-0000-00000000e419");

    // Stub clock is 2026-05-11 12:00Z = 20:00 Taipei.
    private static readonly DateOnly Today = new(2026, 5, 11);

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly RecordingNotify _notify = new();

    public AttendanceCorrectionServiceTests()
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
        db.Database.ExecuteSqlRaw(@"
CREATE TABLE Admin_Principals (
    Id TEXT NOT NULL PRIMARY KEY, Type INTEGER NOT NULL, DisplayName TEXT NOT NULL,
    Email TEXT NULL, Active INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, DeletedAt TEXT NULL);
CREATE TABLE Admin_Roles (
    Id TEXT NOT NULL PRIMARY KEY, Code TEXT NOT NULL DEFAULT '', Name TEXT NOT NULL, IsSystem INTEGER NOT NULL, Description TEXT NULL);
CREATE TABLE Admin_PrincipalRoles (
    PrincipalId TEXT NOT NULL, RoleId TEXT NOT NULL, InheritToMembers INTEGER NOT NULL,
    IncludeSubDepts INTEGER NOT NULL DEFAULT 0, AssignedAt TEXT NOT NULL, AssignedByUserId TEXT NULL,
    PRIMARY KEY (PrincipalId, RoleId));
CREATE TABLE Admin_UserDepts (
    UserId TEXT NOT NULL, DeptId TEXT NOT NULL, IsPrimary INTEGER NOT NULL, PRIMARY KEY (UserId, DeptId));
CREATE TABLE Admin_DeptParents (DeptId TEXT NOT NULL PRIMARY KEY, ParentDeptId TEXT NULL);
CREATE TABLE Admin_GroupMembers (GroupId TEXT NOT NULL, MemberPrincipalId TEXT NOT NULL, MemberType INTEGER NOT NULL, PRIMARY KEY (GroupId, MemberPrincipalId));
CREATE TABLE Admin_UserManagers (UserId TEXT NOT NULL PRIMARY KEY, ManagerUserId TEXT NOT NULL, AssignedAt TEXT NOT NULL);");

        var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        db.SharedPrincipals.AddRange(
            new SharedPrincipal { Id = Bob, Type = SharedPrincipalType.User, DisplayName = "Bob", Email = "bob@acme.example", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Alice, Type = SharedPrincipalType.User, DisplayName = "Alice", Email = "alice@acme.example", Active = true, CreatedAt = now, UpdatedAt = now },
            new SharedPrincipal { Id = Erin, Type = SharedPrincipalType.User, DisplayName = "Erin", Email = "erin@acme.example", Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedUserManagers.Add(new SharedUserManager { UserId = Bob, ManagerUserId = Alice, AssignedAt = now });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private AttendanceCorrectionService NewService(AppDbContext db)
        => new(db, new StubClock(), new PrincipalDirectory(db), _notify);

    private static SubmitCorrectionRequest Req(DateOnly? date = null, PunchType type = PunchType.In, string time = "09:00", string reason = "忘了打卡")
        => new(date ?? Today, type, time, reason);

    [Fact]
    public async Task Submit_Then_Manager_Approve_Inserts_Correction_Punch()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        var dto = await svc.SubmitAsync(Bob, Req(), CancellationToken.None);
        Assert.Equal(CorrectionStatus.Pending, dto.Status);
        Assert.Single(_notify.Sent);   // manager notified

        var decided = await svc.DecideAsync(Alice, dto.Id, new DecideCorrectionRequest(true, "OK"), CancellationToken.None);
        Assert.Equal(CorrectionStatus.Approved, decided.Status);
        Assert.Equal(2, _notify.Sent.Count);   // requester notified

        var punch = await db.AttendancePunches.SingleAsync();
        Assert.Equal(Bob, punch.UserId);
        Assert.Equal(PunchSource.Correction, punch.Source);
        Assert.Equal(Today, punch.LocalDate);
        // 09:00 Taipei = 01:00Z
        Assert.Equal(new DateTime(2026, 5, 11, 1, 0, 0, DateTimeKind.Utc), punch.PunchAt);
    }

    [Fact]
    public async Task Reject_Does_Not_Insert_Punch()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var dto = await svc.SubmitAsync(Bob, Req(), CancellationToken.None);

        var decided = await svc.DecideAsync(Alice, dto.Id, new DecideCorrectionRequest(false, "查無出勤事實"), CancellationToken.None);
        Assert.Equal(CorrectionStatus.Rejected, decided.Status);
        Assert.Empty(db.AttendancePunches);
    }

    [Fact]
    public async Task Unrelated_User_Cannot_Decide_But_Requesters_Manager_Shows_In_Pending()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var dto = await svc.SubmitAsync(Bob, Req(), CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.DecideAsync(Erin, dto.Id, new DecideCorrectionRequest(true, null), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => svc.DecideAsync(Bob, dto.Id, new DecideCorrectionRequest(true, null), CancellationToken.None));

        Assert.Single(await svc.PendingForApproverAsync(Alice, CancellationToken.None));
        Assert.Empty(await svc.PendingForApproverAsync(Erin, CancellationToken.None));
    }

    [Fact]
    public async Task Decide_Twice_Conflicts()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var dto = await svc.SubmitAsync(Bob, Req(), CancellationToken.None);
        await svc.DecideAsync(Alice, dto.Id, new DecideCorrectionRequest(true, null), CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(
            () => svc.DecideAsync(Alice, dto.Id, new DecideCorrectionRequest(false, null), CancellationToken.None));
    }

    [Fact]
    public async Task Submit_Validations()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(reason: " "), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(time: "25:99"), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(date: Today.AddDays(1)), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(date: Today.AddDays(-31)), CancellationToken.None));
        // 21:00 Taipei today is in the future relative to the 20:00 stub clock
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(time: "21:00"), CancellationToken.None));

        await svc.SubmitAsync(Bob, Req(), CancellationToken.None);
        // duplicate pending for same date+type
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(Bob, Req(), CancellationToken.None));
        // other punch type on the same day is fine
        var outDto = await svc.SubmitAsync(Bob, Req(type: PunchType.Out, time: "18:00"), CancellationToken.None);
        Assert.Equal(CorrectionStatus.Pending, outDto.Status);
    }

    private sealed class RecordingNotify : INotifyDispatcher
    {
        public List<NotifyMessage> Sent { get; } = new();
        public Task DispatchAsync(NotifyMessage m, CancellationToken ct = default) { Sent.Add(m); return Task.CompletedTask; }
    }
}

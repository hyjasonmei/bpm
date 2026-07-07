using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Support;
using Bpm.Domain.Entities.Support;
using Bpm.Persistence;
using Bpm.Persistence.Common.Directory;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.SharedIdentity;
using Bpm.Persistence.Support;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Persistence.Support;

/// Report-an-issue is a real feature now: submissions persist, SYSTEM_ADMINs
/// get notified, and admins can walk the status ladder.
public sealed class SupportIssueServiceTests : IDisposable
{
    private static readonly Guid Bob  = Guid.Parse("00000000-0000-0000-0000-000000000b0b");
    private static readonly Guid Jack = Guid.Parse("00000000-0000-0000-0000-00000000d0d0");
    private static readonly Guid RoleAdmin = Guid.Parse("00000000-0000-0000-0000-00000000ad01");

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly RecordingNotify _notify = new();

    public SupportIssueServiceTests()
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
            new SharedPrincipal { Id = Jack, Type = SharedPrincipalType.User, DisplayName = "Jack", Email = "jack@acme.example", Active = true, CreatedAt = now, UpdatedAt = now });
        db.SharedRoles.Add(new SharedRole { Id = RoleAdmin, Code = "SYSTEM_ADMIN", Name = "系統管理員", IsSystem = true });
        db.SharedPrincipalRoles.Add(new SharedPrincipalRole { PrincipalId = Jack, RoleId = RoleAdmin, InheritToMembers = false, AssignedAt = now });
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private SupportIssueService NewService(AppDbContext db)
        => new(db, new StubClock(), new PrincipalDirectory(db), _notify);

    [Fact]
    public async Task Submit_Persists_And_Notifies_Admins()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);

        var dto = await svc.SubmitAsync(Bob,
            new SubmitIssueRequest("bug", "送出按鈕沒反應", "在請假表單按送出沒有任何反應", "@bob", "/apply/LEAVE"),
            "TestAgent/1.0", CancellationToken.None);

        Assert.Equal(SupportIssueStatus.New, dto.Status);
        Assert.Equal("Bob", dto.UserName);
        Assert.Equal("TestAgent/1.0", dto.UserAgent);

        var msg = Assert.Single(_notify.Sent);
        Assert.Contains(msg.Recipients, r => r.UserId == Jack);
        Assert.Contains("送出按鈕沒反應", msg.Subject);
    }

    [Fact]
    public async Task List_Filters_By_Status_And_SetStatus_Walks_Ladder()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        var dto = await svc.SubmitAsync(Bob, new SubmitIssueRequest("question", "怎麼查舊案件", "Search 頁找不到入口", null, "/search"), null, CancellationToken.None);

        Assert.Single(await svc.ListAsync(SupportIssueStatus.New, CancellationToken.None));
        Assert.Empty(await svc.ListAsync(SupportIssueStatus.Closed, CancellationToken.None));

        var ack = await svc.SetStatusAsync(dto.Id, SupportIssueStatus.Acknowledged, CancellationToken.None);
        Assert.Equal(SupportIssueStatus.Acknowledged, ack.Status);
        Assert.Empty(await svc.ListAsync(SupportIssueStatus.New, CancellationToken.None));

        await Assert.ThrowsAsync<NotFoundException>(
            () => svc.SetStatusAsync(Guid.NewGuid(), SupportIssueStatus.Closed, CancellationToken.None));
    }

    [Fact]
    public async Task Submit_Requires_Title_And_Description()
    {
        await using var db = new AppDbContext(_options);
        var svc = NewService(db);
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SubmitAsync(Bob, new SubmitIssueRequest("bug", " ", "desc", null, null), null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.SubmitAsync(Bob, new SubmitIssueRequest("bug", "title", "", null, null), null, CancellationToken.None));
    }

    private sealed class RecordingNotify : INotifyDispatcher
    {
        public List<NotifyMessage> Sent { get; } = new();
        public Task DispatchAsync(NotifyMessage m, CancellationToken ct = default) { Sent.Add(m); return Task.CompletedTask; }
    }
}

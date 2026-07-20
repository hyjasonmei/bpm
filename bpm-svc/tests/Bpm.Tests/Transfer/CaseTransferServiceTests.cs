using Bpm.Application.Notifications;
using Bpm.Application.Transfer;
using Bpm.Domain.Features.OVERTIME.V1;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Bpm.Persistence.Transfer;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Transfer;

/// <summary>
/// Unit tests for the shared end-user case-transfer primitive (轉簽). Real EF
/// (in-memory SQLite) + self-only authorizer + capturing notify sink; uses
/// OVERTIME_V1_Case as the vehicle (personal manager stage, full column set).
/// Validation order and error codes mirror
/// docs/superpowers/specs/2026-07-19-case-transfer-design.md.
/// </summary>
public sealed class CaseTransferServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"); // manager / current assignee
    private static readonly Guid Bob   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"); // submitter
    private static readonly Guid Carol = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"); // transfer target
    private static readonly Guid Dave  = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"); // inactive user
    private static readonly Guid Case1 = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    public CaseTransferServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
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
);");
        SeedPrincipal(db, Alice, "Alice", "alice@acme.example", active: true);
        SeedPrincipal(db, Bob, "Bob", "bob@acme.example", active: true);
        SeedPrincipal(db, Carol, "Carol", "carol@acme.example", active: true);
        SeedPrincipal(db, Dave, "Dave", "dave@acme.example", active: false);
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private static void SeedPrincipal(AppDbContext db, Guid id, string name, string email, bool active)
        => db.SharedPrincipals.Add(new SharedPrincipal
        {
            Id = id,
            Type = SharedPrincipalType.User,
            DisplayName = name,
            Email = email,
            Active = active,
        });

    private sealed class CaptureNotify : INotifyDispatcher
    {
        public List<NotifyMessage> Sent { get; } = new();
        public Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static void SeedCase(AppDbContext db,
        Guid? assignee = null, string? roleCode = null, DateTime? completedAt = null)
    {
        db.Set<OVERTIME_V1_Case>().Add(new OVERTIME_V1_Case
        {
            Id = Case1,
            SubmitterUserId = Bob,
            OvertimeDate = new DateOnly(2026, 7, 10),
            OvertimeReason = "趕專案",
            Status = OVERTIME_V1_CaseStatus.PendingManager,
            CurrentAssigneeUserId = assignee,
            CurrentAssigneeRoleCode = roleCode,
            SubmittedAt = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc),
            LastActivityAt = new DateTime(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc),
            CompletedAt = completedAt,
        });
        db.SaveChanges();
    }

    private static CaseTransferService NewService(AppDbContext db, CaptureNotify? sink = null)
        => new(db, new TestActorAuthorizer(), new StubClock(), sink ?? new CaptureNotify(),
            NullLogger<CaseTransferService>.Instance);

    // ── validation rules (spec order) ────────────────────────────────────────

    [Fact]
    public async Task Transfer_unknown_flow_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var r = await NewService(db).TransferAsync("NOPE", Case1, Alice, Carol, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("unknown_flow", r.Error);
    }

    [Fact]
    public async Task Transfer_closed_case_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice, completedAt: DateTime.UtcNow);
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Carol, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("not_found_or_closed", r.Error);
    }

    [Fact]
    public async Task Transfer_role_stage_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: null, roleCode: "HR_MANAGER");
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Carol, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("role_stage_not_transferable", r.Error);
    }

    [Fact]
    public async Task Transfer_by_non_assignee_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Bob, Carol, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("not_current_assignee", r.Error);
    }

    [Fact]
    public async Task Transfer_to_inactive_target_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Dave, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("target_not_active", r.Error);
    }

    [Fact]
    public async Task Transfer_to_current_assignee_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Alice, "理由", default);
        Assert.False(r.Ok);
        Assert.Equal("target_is_current", r.Error);
    }

    [Fact]
    public async Task Transfer_without_reason_fails()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Carol, "   ", default);
        Assert.False(r.Ok);
        Assert.Equal("reason_required", r.Error);
    }

    // ── success ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_success_updates_assignee_and_logs()
    {
        await using (var db = new AppDbContext(_options))
        {
            SeedCase(db, assignee: Alice);
            var r = await NewService(db).TransferAsync("OVERTIME", Case1, Alice, Carol, " 出差不在，請 Carol 代審 ", default);
            Assert.True(r.Ok);
            Assert.Null(r.Error);
        }

        await using (var db = new AppDbContext(_options))
        {
            var c = await db.Set<OVERTIME_V1_Case>().SingleAsync(x => x.Id == Case1);
            Assert.Equal(Carol, c.CurrentAssigneeUserId);
            Assert.Null(c.CurrentAssigneeRoleCode);

            var log = await db.CaseTransferLogs.SingleAsync();
            Assert.Equal("OVERTIME", log.FlowCode);
            Assert.Equal(1, log.FlowVersion);
            Assert.Equal(Case1, log.CaseId);
            Assert.Equal(Alice, log.FromUserId);
            Assert.Equal(Carol, log.ToUserId);
            Assert.Equal(Alice, log.OperatorUserId);
            Assert.Equal("出差不在，請 Carol 代審", log.Reason);
        }
    }

    [Fact]
    public async Task Transfer_success_notifies_target_and_submitter()
    {
        await using var db = new AppDbContext(_options);
        SeedCase(db, assignee: Alice);
        var sink = new CaptureNotify();
        var r = await NewService(db, sink).TransferAsync("OVERTIME", Case1, Alice, Carol, "代審", default);
        Assert.True(r.Ok);

        var msg = Assert.Single(sink.Sent);
        Assert.Equal("OVERTIME_V1.notify_transfer", msg.SourceId);
        Assert.Contains(msg.Recipients, x => x.UserId == Carol);
        Assert.Contains(msg.Recipients, x => x.UserId == Bob);
        Assert.Equal(Case1.ToString(), msg.Context?["caseId"]);
        Assert.Equal("OVERTIME", msg.Context?["flowCode"]);
        Assert.Equal("1", msg.Context?["flowVersion"]);
    }

    // ── candidates ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Candidates_filters_to_active_users_and_matches_query()
    {
        await using var db = new AppDbContext(_options);
        var all = await NewService(db).CandidatesAsync(null, default);
        Assert.Equal(3, all.Count);                       // Dave inactive → excluded
        Assert.DoesNotContain(all, x => x.UserId == Dave);

        var hit = await NewService(db).CandidatesAsync("caro", default);
        var c = Assert.Single(hit);
        Assert.Equal(Carol, c.UserId);
        Assert.Equal("Carol", c.Name);
    }
}

using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.CONTRACT_REVIEW.V1;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Parallel;
using Bpm.Persistence;
using Bpm.Persistence.Features.CONTRACT_REVIEW.V1;
using Bpm.Persistence.Parallel;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Flow-level tests for CONTRACT_REVIEW V1 (合約審查). Exercises the full state
/// machine over the real EF + real <see cref="ParallelApprovalService"/> primitive:
/// LEGAL+FINANCE 並簽 → gateway_decision → LEGAL_MANAGER 定案歸檔 → Completed, plus the
/// 任一退回 → 重新送審 loop, abandon/withdraw, wrong-actor 403 and state 409.
/// </summary>
public sealed class CONTRACT_REVIEW_V1_FlowTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Sam = Guid.Parse("11111111-0000-0000-0000-000000000001");  // submitter
    private static readonly Guid Lena = Guid.Parse("11111111-0000-0000-0000-000000000002"); // LEGAL
    private static readonly Guid Fred = Guid.Parse("11111111-0000-0000-0000-000000000003"); // FINANCE
    private static readonly Guid Mona = Guid.Parse("11111111-0000-0000-0000-000000000004"); // LEGAL_MANAGER
    private static readonly Guid Nick = Guid.Parse("11111111-0000-0000-0000-000000000005"); // no role

    public CONTRACT_REVIEW_V1_FlowTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    // ── test doubles ────────────────────────────────────────────────────────
    private sealed class RecordingNotify : INotifyDispatcher
    {
        public List<NotifyMessage> Sent { get; } = new();
        public Task DispatchAsync(NotifyMessage m, CancellationToken ct = default) { Sent.Add(m); return Task.CompletedTask; }
    }

    /// <summary>Role-aware authorizer backed by a fixed user→roles map (no delegation).</summary>
    private sealed class RoleMapAuthorizer : IActorAuthorizer
    {
        private readonly Dictionary<Guid, HashSet<string>> _roles;
        public RoleMapAuthorizer(Dictionary<Guid, HashSet<string>> roles) => _roles = roles;
        public Task<bool> CanActAsync(Guid requiredUserId, Guid caller, CancellationToken ct = default)
            => Task.FromResult(requiredUserId == caller);
        public Task<bool> CanActAsync(Guid? requiredUserId, string? roleCode, Guid caller, CancellationToken ct = default)
        {
            if (requiredUserId is { } u && u == caller) return Task.FromResult(true);
            if (!string.IsNullOrEmpty(roleCode) && _roles.TryGetValue(caller, out var r) && r.Contains(roleCode))
                return Task.FromResult(true);
            return Task.FromResult(false);
        }
    }

    /// <summary>Directory backed by a fixed role→users + user→display map.</summary>
    private sealed class MapDirectory : IPrincipalDirectory
    {
        private readonly Dictionary<string, List<Guid>> _roleUsers;
        private readonly Dictionary<Guid, string> _names;
        public MapDirectory(Dictionary<string, List<Guid>> roleUsers, Dictionary<Guid, string> names)
        { _roleUsers = roleUsers; _names = names; }

        public Task<PrincipalInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<PrincipalInfo?>(_names.TryGetValue(id, out var n)
                ? new PrincipalInfo(id, PrincipalKind.User, n, $"{n}@acme.example", true) : null);
        public Task<IReadOnlyDictionary<Guid, PrincipalInfo>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, PrincipalInfo>>(ids
                .Where(_names.ContainsKey)
                .ToDictionary(id => id, id => new PrincipalInfo(id, PrincipalKind.User, _names[id], $"{_names[id]}@acme.example", true)));
        public Task<Guid?> FindFirstUserInRoleAsync(string role, CancellationToken ct = default)
            => Task.FromResult(_roleUsers.TryGetValue(role, out var u) && u.Count > 0 ? u[0] : (Guid?)null);
        public Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string role, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(_roleUsers.TryGetValue(role, out var u) ? u : new List<Guid>());
        public Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(_roleUsers
                .Where(kv => kv.Value.Contains(userId)).Select(kv => kv.Key).ToHashSet());
    }

    private static MapDirectory Directory() => new(
        roleUsers: new()
        {
            [CONTRACT_REVIEW_V1_Service.LegalRole] = new() { Lena },
            [CONTRACT_REVIEW_V1_Service.FinanceRole] = new() { Fred },
            [CONTRACT_REVIEW_V1_Service.LegalManagerRole] = new() { Mona },
        },
        names: new() { [Sam] = "Sam", [Lena] = "Lena", [Fred] = "Fred", [Mona] = "Mona", [Nick] = "Nick" });

    private static RoleMapAuthorizer Authorizer() => new(new()
    {
        [Lena] = new() { CONTRACT_REVIEW_V1_Service.LegalRole },
        [Fred] = new() { CONTRACT_REVIEW_V1_Service.FinanceRole },
        [Mona] = new() { CONTRACT_REVIEW_V1_Service.LegalManagerRole },
        [Sam] = new(),
        [Nick] = new(),
    });

    private (CONTRACT_REVIEW_V1_Service svc, RecordingNotify notify) NewService(AppDbContext db)
    {
        var parallel = new ParallelApprovalService(db, new StubClock(), Authorizer());
        var store = new CONTRACT_REVIEW_V1_CaseStore(db);
        var notify = new RecordingNotify();
        var svc = new CONTRACT_REVIEW_V1_Service(store, parallel, Authorizer(), new StubClock(), notify, Directory());
        return (svc, notify);
    }

    private ParallelApprovalService NewParallel(AppDbContext db) => new(db, new StubClock(), Authorizer());

    private static CONTRACT_REVIEW_V1_Service.SubmitInput Input() => new(
        Sam, "ACME Corp", "ACME 年度供貨合約", 500000m,
        new DateOnly(2026, 8, 1), new DateOnly(2027, 7, 31), Guid.NewGuid(), "首年合約");

    private async Task<(Guid legalSlot, Guid financeSlot)> SlotsOf(AppDbContext db, Guid caseId, int round = 1)
    {
        var g = await NewParallel(db).GetAsync(caseId, CONTRACT_REVIEW_V1_Service.ReviewGatewayKey(round), default);
        return (g!.Slots.First(s => s.AssigneeRoleCode == CONTRACT_REVIEW_V1_Service.LegalRole).Id,
                g.Slots.First(s => s.AssigneeRoleCode == CONTRACT_REVIEW_V1_Service.FinanceRole).Id);
    }

    // ── submit ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Submit_opens_two_pending_slots_legal_and_finance()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);

        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview, c.Status);
        Assert.Equal(1, c.CurrentRound);
        var g = await NewParallel(db).GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayKey(1), default);
        Assert.NotNull(g);
        Assert.Equal(2, g!.TotalSlots);
        Assert.Equal(2, g.Threshold);
        Assert.Contains(g.Slots, s => s.NodeId == CONTRACT_REVIEW_V1_Service.LegalNodeId && s.AssigneeRoleCode == "LEGAL");
        Assert.Contains(g.Slots, s => s.NodeId == CONTRACT_REVIEW_V1_Service.FinanceNodeId && s.AssigneeRoleCode == "FINANCE");
    }

    [Fact]
    public async Task Submit_notifies_both_reviewers()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        await svc.SubmitAsync(Input(), default);

        var msg = Assert.Single(notify.Sent, m => m.SourceId.EndsWith("notify_submit_reviewers"));
        Assert.Contains(msg.Recipients, r => r.UserId == Lena);
        Assert.Contains(msg.Recipients, r => r.UserId == Fred);
        Assert.Equal("CONTRACT_REVIEW", msg.Context!["flowCode"]);
        Assert.Contains("送審通知", msg.Subject);
    }

    // ── happy path (both approve → legal mgr → completed) ──────────────────────
    [Fact]
    public async Task Both_approve_moves_to_pending_legal_manager_then_manager_completes()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, finance) = await SlotsOf(db, c.Id);

        var afterLegal = await svc.DecideAsync(c.Id, legal, Lena, approve: true, "法務OK", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview, afterLegal.Status);

        var afterFinance = await svc.DecideAsync(c.Id, finance, Fred, approve: true, "財務OK", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager, afterFinance.Status);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_approve_legal_mgr") && m.Recipients.Any(r => r.UserId == Mona));

        var done = await svc.LegalManagerDecideAsync(c.Id, Mona, approve: true, "歸檔完成", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Completed, done.Status);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.LegalManagerApproved);
        Assert.Equal(Mona, done.LegalManagerUserId);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_complete_submitter") && m.Recipients.Any(r => r.UserId == Sam));
    }

    // ── reject → resubmit loop ────────────────────────────────────────────────
    [Fact]
    public async Task Any_reject_moves_to_resubmit_required_and_skips_other_and_notifies_submitter()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, _) = await SlotsOf(db, c.Id);

        var after = await svc.DecideAsync(c.Id, legal, Lena, approve: false, "條款有疑慮", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired, after.Status);

        var g = await NewParallel(db).GetAsync(c.Id, CONTRACT_REVIEW_V1_Service.ReviewGatewayKey(1), default);
        Assert.Equal(SlotDecision.Rejected, g!.Slots.First(s => s.AssigneeRoleCode == "LEGAL").Decision);
        Assert.Equal(SlotDecision.Skipped, g.Slots.First(s => s.AssigneeRoleCode == "FINANCE").Decision);

        var reject = Assert.Single(notify.Sent, m => m.SourceId.EndsWith("notify_reject_submitter"));
        Assert.Contains(reject.Recipients, r => r.UserId == Sam);
        Assert.Contains("條款有疑慮", reject.Body);
        Assert.Contains("Lena", reject.Body);
    }

    [Fact]
    public async Task Resubmit_reopens_fresh_parallel_round_then_full_approval_completes()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal1, _) = await SlotsOf(db, c.Id, 1);
        await svc.DecideAsync(c.Id, legal1, Lena, approve: false, "請補附件", default);

        var revised = await svc.ResubmitAsync(c.Id, Sam, Input(), "已補上附件並修正條款", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview, revised.Status);
        Assert.Equal(2, revised.CurrentRound);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_resubmit_reviewers"));

        // Round-2 group is distinct & Open.
        var (legal2, finance2) = await SlotsOf(db, c.Id, 2);
        Assert.NotEqual(legal1, legal2);
        await svc.DecideAsync(c.Id, legal2, Lena, approve: true, null, default);
        var afterFinance = await svc.DecideAsync(c.Id, finance2, Fred, approve: true, null, default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager, afterFinance.Status);

        var done = await svc.LegalManagerDecideAsync(c.Id, Mona, approve: true, null, default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Completed, done.Status);
    }

    [Fact]
    public async Task Legal_manager_reject_sends_back_to_resubmit_required()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, finance) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, true, null, default);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);

        var after = await svc.LegalManagerDecideAsync(c.Id, Mona, approve: false, "歸檔前需補用印頁", default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired, after.Status);
        Assert.False(after.LegalManagerApproved);
    }

    // ── abandon / withdraw ────────────────────────────────────────────────────
    [Fact]
    public async Task Submitter_abandons_from_resubmit_required_cancels_case()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, false, "no", default);

        var cancelled = await svc.CancelAsync(c.Id, Sam, default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
    }

    [Fact]
    public async Task Submitter_withdraws_during_parallel_review_cancels_case()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);

        var cancelled = await svc.CancelAsync(c.Id, Sam, default);
        Assert.Equal(CONTRACT_REVIEW_V1_CaseStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task Withdraw_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CancelAsync(c.Id, Nick, default));
    }

    [Fact]
    public async Task Withdraw_after_completed_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, finance) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, true, null, default);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.LegalManagerDecideAsync(c.Id, Mona, true, null, default);

        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelAsync(c.Id, Sam, default));
    }

    // ── authorization / state guards ──────────────────────────────────────────
    [Fact]
    public async Task Parallel_decision_by_wrong_actor_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, _) = await SlotsOf(db, c.Id);
        // Fred holds FINANCE, not LEGAL — cannot act on the LEGAL slot.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DecideAsync(c.Id, legal, Fred, true, null, default));
    }

    [Fact]
    public async Task Legal_manager_decision_by_wrong_actor_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, finance) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, true, null, default);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        // Lena is LEGAL, not LEGAL_MANAGER.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.LegalManagerDecideAsync(c.Id, Lena, true, null, default));
    }

    [Fact]
    public async Task Parallel_decision_after_resolution_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, finance) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, true, null, default);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        // Case now PendingLegalManager — the parallel step is closed.
        await Assert.ThrowsAsync<ConflictException>(() => svc.DecideAsync(c.Id, legal, Lena, true, null, default));
    }

    [Fact]
    public async Task Legal_manager_decision_before_parallel_completes_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        await Assert.ThrowsAsync<ConflictException>(() => svc.LegalManagerDecideAsync(c.Id, Mona, true, null, default));
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (legal, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, false, null, default);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ResubmitAsync(c.Id, Nick, Input(), "x", default));
    }

    // ── validation ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Submit_rejects_period_end_before_start()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var bad = Input() with { PeriodStart = new DateOnly(2027, 1, 1), PeriodEnd = new DateOnly(2026, 1, 1) };
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(bad, default));
    }

    [Fact]
    public async Task Submit_rejects_missing_draft_file()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var bad = Input() with { DraftFileId = null };
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(bad, default));
    }

    // ── inbox provider ────────────────────────────────────────────────────────
    [Fact]
    public async Task Inbox_surfaces_case_to_both_reviewers_then_moves_to_legal_manager()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var inbox = new CONTRACT_REVIEW_V1_InboxProvider(new CONTRACT_REVIEW_V1_CaseStore(db), NewParallel(db), Directory());
        var c = await svc.SubmitAsync(Input(), default);

        Assert.Single(await inbox.GetPendingAsync(Lena, default), r => r.CaseId == c.Id);
        Assert.Single(await inbox.GetPendingAsync(Fred, default), r => r.CaseId == c.Id);
        Assert.Empty(await inbox.GetPendingAsync(Mona, default));
        Assert.Single(await inbox.GetMineAsync(Sam, default), r => r.CaseId == c.Id);

        var (legal, finance) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, legal, Lena, true, null, default);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);

        // Now pending on LEGAL_MANAGER; reviewers no longer see it.
        Assert.Empty(await inbox.GetPendingAsync(Lena, default));
        Assert.Single(await inbox.GetPendingAsync(Mona, default), r => r.CaseId == c.Id);
    }

    [Fact]
    public async Task Inbox_hides_withdrawn_case_from_reviewers()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var inbox = new CONTRACT_REVIEW_V1_InboxProvider(new CONTRACT_REVIEW_V1_CaseStore(db), NewParallel(db), Directory());
        var c = await svc.SubmitAsync(Input(), default);
        await svc.CancelAsync(c.Id, Sam, default);

        // Parallel slots are still technically Open in the primitive, but the
        // provider filters on case status, so a withdrawn case never leaks.
        Assert.Empty(await inbox.GetPendingAsync(Lena, default));
    }

    // ── notification templates ────────────────────────────────────────────────
    [Fact]
    public void Templates_render_subject_and_body_substitutions()
    {
        var submit = CONTRACT_REVIEW_V1_NotificationTemplates.RenderSubmitReviewers("Sam", "ACME Corp", "供貨合約", "NT$ 500,000", "/cases/contract_review/x");
        Assert.Contains("ACME Corp", submit.Subject);
        Assert.Contains("Sam", submit.Body);
        Assert.Contains("NT$ 500,000", submit.Body);

        var reject = CONTRACT_REVIEW_V1_NotificationTemplates.RenderRejectSubmitter("Sam", "ACME Corp", "Lena", "條款問題", "/u");
        Assert.Contains("Lena", reject.Body);
        Assert.Contains("條款問題", reject.Body);

        var mgr = CONTRACT_REVIEW_V1_NotificationTemplates.RenderApproveLegalMgr("ACME Corp", "供貨合約", "NT$ 1", "/u");
        Assert.Contains("定案歸檔", mgr.Subject);

        var done = CONTRACT_REVIEW_V1_NotificationTemplates.RenderCompleteSubmitter("Sam", "ACME Corp", "供貨合約", "NT$ 1", "2026-08-01 12:00", "/u");
        Assert.Contains("2026-08-01 12:00", done.Body);

        var resub = CONTRACT_REVIEW_V1_NotificationTemplates.RenderResubmitReviewers("Sam", "ACME Corp", "供貨合約", "NT$ 1", "已修正", "/u");
        Assert.Contains("已修正", resub.Body);
    }
}

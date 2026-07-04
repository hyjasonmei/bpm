using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Features.COMMITTEE_REVIEW.V1;
using Bpm.Application.Notifications;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Parallel;
using Bpm.Persistence;
using Bpm.Persistence.Features.COMMITTEE_REVIEW.V1;
using Bpm.Persistence.Parallel;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bpm.Tests.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Flow-level tests for COMMITTEE_REVIEW V1 (委員會審議). Exercises the full state
/// machine over the real EF + real <see cref="ParallelApprovalService"/> primitive:
/// 財務+採購+資訊 三委員並簽（門檻 2/3）→ 執行長最終裁決 → Completed / Rejected（終局），
/// plus the 任一退回 → 重新送審 loop, abandon/withdraw, wrong-actor 403 and state 409.
/// </summary>
public sealed class COMMITTEE_REVIEW_V1_FlowTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;

    private static readonly Guid Sam = Guid.Parse("22222222-0000-0000-0000-000000000001");  // submitter
    private static readonly Guid Fred = Guid.Parse("22222222-0000-0000-0000-000000000002"); // FINANCE
    private static readonly Guid Gina = Guid.Parse("22222222-0000-0000-0000-000000000003"); // PROCUREMENT
    private static readonly Guid Dave = Guid.Parse("22222222-0000-0000-0000-000000000004"); // IT_COMMITTEE
    private static readonly Guid Alice = Guid.Parse("22222222-0000-0000-0000-000000000005"); // CEO
    private static readonly Guid Nick = Guid.Parse("22222222-0000-0000-0000-000000000006"); // no role

    public COMMITTEE_REVIEW_V1_FlowTests()
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
            [COMMITTEE_REVIEW_V1_Service.FinanceRole] = new() { Fred },
            [COMMITTEE_REVIEW_V1_Service.ProcurementRole] = new() { Gina },
            [COMMITTEE_REVIEW_V1_Service.ItRole] = new() { Dave },
            [COMMITTEE_REVIEW_V1_Service.CeoRole] = new() { Alice },
        },
        names: new() { [Sam] = "Sam", [Fred] = "Fred", [Gina] = "Gina", [Dave] = "Dave", [Alice] = "Alice", [Nick] = "Nick" });

    private static RoleMapAuthorizer Authorizer() => new(new()
    {
        [Fred] = new() { COMMITTEE_REVIEW_V1_Service.FinanceRole },
        [Gina] = new() { COMMITTEE_REVIEW_V1_Service.ProcurementRole },
        [Dave] = new() { COMMITTEE_REVIEW_V1_Service.ItRole },
        [Alice] = new() { COMMITTEE_REVIEW_V1_Service.CeoRole },
        [Sam] = new(),
        [Nick] = new(),
    });

    private (COMMITTEE_REVIEW_V1_Service svc, RecordingNotify notify) NewService(AppDbContext db)
    {
        var parallel = new ParallelApprovalService(db, new StubClock(), Authorizer());
        var store = new COMMITTEE_REVIEW_V1_CaseStore(db);
        var notify = new RecordingNotify();
        var svc = new COMMITTEE_REVIEW_V1_Service(store, parallel, Authorizer(), new StubClock(), notify, Directory());
        return (svc, notify);
    }

    private ParallelApprovalService NewParallel(AppDbContext db) => new(db, new StubClock(), Authorizer());

    private static COMMITTEE_REVIEW_V1_Service.SubmitInput Input() => new(
        Sam, "新一代 ERP 系統採購案", "major_procurement", 5_000_000m,
        "汰換老舊系統，提升作業效率 30%", new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31), Guid.NewGuid(), "首年導入");

    private async Task<(Guid finance, Guid procurement, Guid it)> SlotsOf(AppDbContext db, Guid caseId, int round = 1)
    {
        var g = await NewParallel(db).GetAsync(caseId, COMMITTEE_REVIEW_V1_Service.ReviewGatewayKey(round), default);
        return (g!.Slots.First(s => s.AssigneeRoleCode == COMMITTEE_REVIEW_V1_Service.FinanceRole).Id,
                g.Slots.First(s => s.AssigneeRoleCode == COMMITTEE_REVIEW_V1_Service.ProcurementRole).Id,
                g.Slots.First(s => s.AssigneeRoleCode == COMMITTEE_REVIEW_V1_Service.ItRole).Id);
    }

    // ── submit ──────────────────────────────────────────────────────────────
    [Fact]
    public async Task Submit_opens_three_pending_slots_with_quorum_threshold()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);

        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview, c.Status);
        Assert.Equal(1, c.CurrentRound);
        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.ReviewGatewayKey(1), default);
        Assert.NotNull(g);
        Assert.Equal(3, g!.TotalSlots);
        Assert.Equal(2, g.Threshold);
        Assert.Contains(g.Slots, s => s.NodeId == COMMITTEE_REVIEW_V1_Service.FinanceNodeId && s.AssigneeRoleCode == "FINANCE");
        Assert.Contains(g.Slots, s => s.NodeId == COMMITTEE_REVIEW_V1_Service.ProcurementNodeId && s.AssigneeRoleCode == "PROCUREMENT");
        Assert.Contains(g.Slots, s => s.NodeId == COMMITTEE_REVIEW_V1_Service.ItNodeId && s.AssigneeRoleCode == "IT_COMMITTEE");
    }

    [Fact]
    public async Task Submit_notifies_all_three_committee_members()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        await svc.SubmitAsync(Input(), default);

        var msg = Assert.Single(notify.Sent, m => m.SourceId.EndsWith("notify_submit_committee"));
        Assert.Contains(msg.Recipients, r => r.UserId == Fred);
        Assert.Contains(msg.Recipients, r => r.UserId == Gina);
        Assert.Contains(msg.Recipients, r => r.UserId == Dave);
        Assert.Equal("COMMITTEE_REVIEW", msg.Context!["flowCode"]);
        Assert.Contains("新審議案待審", msg.Subject);
    }

    // ── happy path: quorum (2/3) → CEO approve → completed ──────────────────────
    [Fact]
    public async Task Two_of_three_approve_moves_to_ceo_third_skipped_then_ceo_completes()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, _) = await SlotsOf(db, c.Id);

        var afterFinance = await svc.DecideAsync(c.Id, finance, Fred, approve: true, "財務OK", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview, afterFinance.Status);

        var afterProcurement = await svc.DecideAsync(c.Id, procurement, Gina, approve: true, "採購OK", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo, afterProcurement.Status);

        // Quorum met at 2/3 → the third (IT) slot is Skipped.
        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.ReviewGatewayKey(1), default);
        Assert.Equal(SlotDecision.Skipped, g!.Slots.First(s => s.AssigneeRoleCode == "IT_COMMITTEE").Decision);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_ceo_on_assign") && m.Recipients.Any(r => r.UserId == Alice));

        var done = await svc.CeoDecideAsync(c.Id, Alice, approve: true, "核定", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Completed, done.Status);
        Assert.NotNull(done.CompletedAt);
        Assert.True(done.CeoApproved);
        Assert.Equal(Alice, done.CeoUserId);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_approved_to_applicant") && m.Recipients.Any(r => r.UserId == Sam));
    }

    [Fact]
    public async Task All_three_approve_also_reaches_ceo()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, it) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        // Third decision after quorum already met should conflict (group resolved).
        var afterSecond = await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo, afterSecond.Status);
        await Assert.ThrowsAsync<ConflictException>(() => svc.DecideAsync(c.Id, it, Dave, true, null, default));
    }

    // ── CEO reject is terminal (no resubmit) ───────────────────────────────────
    [Fact]
    public async Task Ceo_reject_is_terminal_rejected_not_resubmit()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);

        var after = await svc.CeoDecideAsync(c.Id, Alice, approve: false, "預算需求不符政策", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Rejected, after.Status);
        Assert.NotNull(after.CompletedAt);
        Assert.False(after.CeoApproved);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_rejected_to_applicant") && m.Recipients.Any(r => r.UserId == Sam));
    }

    [Fact]
    public async Task Ceo_reject_requires_comment()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CeoDecideAsync(c.Id, Alice, approve: false, "  ", default));
    }

    // ── reject → resubmit loop ────────────────────────────────────────────────
    [Fact]
    public async Task Any_reject_moves_to_resubmit_required_and_skips_others_and_notifies_submitter()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);

        var after = await svc.DecideAsync(c.Id, finance, Fred, approve: false, "效益量化不足", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.ResubmitRequired, after.Status);

        var g = await NewParallel(db).GetAsync(c.Id, COMMITTEE_REVIEW_V1_Service.ReviewGatewayKey(1), default);
        Assert.Equal(SlotDecision.Rejected, g!.Slots.First(s => s.AssigneeRoleCode == "FINANCE").Decision);
        Assert.Equal(SlotDecision.Skipped, g.Slots.First(s => s.AssigneeRoleCode == "PROCUREMENT").Decision);
        Assert.Equal(SlotDecision.Skipped, g.Slots.First(s => s.AssigneeRoleCode == "IT_COMMITTEE").Decision);

        var ret = Assert.Single(notify.Sent, m => m.SourceId.EndsWith("notify_return_to_applicant"));
        Assert.Contains(ret.Recipients, r => r.UserId == Sam);
        Assert.Contains("效益量化不足", ret.Body);
    }

    [Fact]
    public async Task Committee_decide_reject_requires_comment()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);
        await Assert.ThrowsAsync<ValidationException>(() => svc.DecideAsync(c.Id, finance, Fred, approve: false, "", default));
    }

    [Fact]
    public async Task Resubmit_reopens_fresh_round_of_three_then_quorum_and_ceo_completes()
    {
        await using var db = new AppDbContext(_options);
        var (svc, notify) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance1, _, _) = await SlotsOf(db, c.Id, 1);
        await svc.DecideAsync(c.Id, finance1, Fred, approve: false, "請補資料", default);

        var revised = await svc.ResubmitAsync(c.Id, Sam, Input(), "已補上效益量化與替代方案比較", default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview, revised.Status);
        Assert.Equal(2, revised.CurrentRound);
        Assert.Contains(notify.Sent, m => m.SourceId.EndsWith("notify_resubmit_committee"));

        // Round-2 group is distinct & Open.
        var (finance2, procurement2, _) = await SlotsOf(db, c.Id, 2);
        Assert.NotEqual(finance1, finance2);
        await svc.DecideAsync(c.Id, finance2, Fred, true, null, default);
        var afterSecond = await svc.DecideAsync(c.Id, procurement2, Gina, true, null, default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo, afterSecond.Status);

        var done = await svc.CeoDecideAsync(c.Id, Alice, approve: true, null, default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Completed, done.Status);
    }

    [Fact]
    public async Task Resubmit_requires_revision_note()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, false, "no", default);
        await Assert.ThrowsAsync<ValidationException>(() => svc.ResubmitAsync(c.Id, Sam, Input(), "  ", default));
    }

    // ── abandon / withdraw ────────────────────────────────────────────────────
    [Fact]
    public async Task Submitter_abandons_from_resubmit_required_cancels_case()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, false, "no", default);

        var cancelled = await svc.CancelAsync(c.Id, Sam, default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CompletedAt);
    }

    [Fact]
    public async Task Submitter_withdraws_during_parallel_review_cancels_case()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);

        var cancelled = await svc.CancelAsync(c.Id, Sam, default);
        Assert.Equal(COMMITTEE_REVIEW_V1_CaseStatus.Cancelled, cancelled.Status);
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
    public async Task Withdraw_after_ceo_rejected_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);
        await svc.CeoDecideAsync(c.Id, Alice, approve: false, "否決", default);

        await Assert.ThrowsAsync<ConflictException>(() => svc.CancelAsync(c.Id, Sam, default));
    }

    // ── authorization / state guards ──────────────────────────────────────────
    [Fact]
    public async Task Committee_decision_by_wrong_actor_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);
        // Gina holds PROCUREMENT, not FINANCE — cannot act on the FINANCE slot.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.DecideAsync(c.Id, finance, Gina, true, null, default));
    }

    [Fact]
    public async Task Ceo_decision_by_wrong_actor_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);
        // Fred is FINANCE, not CEO.
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.CeoDecideAsync(c.Id, Fred, true, null, default));
    }

    [Fact]
    public async Task Committee_decision_after_resolution_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, procurement, it) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);
        // Case now PendingCeo — the parallel step is closed.
        await Assert.ThrowsAsync<ConflictException>(() => svc.DecideAsync(c.Id, it, Dave, true, null, default));
    }

    [Fact]
    public async Task Ceo_decision_before_quorum_conflicts()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        await Assert.ThrowsAsync<ConflictException>(() => svc.CeoDecideAsync(c.Id, Alice, true, null, default));
    }

    [Fact]
    public async Task Resubmit_by_non_submitter_is_forbidden()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var c = await svc.SubmitAsync(Input(), default);
        var (finance, _, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, false, "no", default);
        await Assert.ThrowsAsync<ForbiddenException>(() => svc.ResubmitAsync(c.Id, Nick, Input(), "x", default));
    }

    // ── validation ────────────────────────────────────────────────────────────
    [Fact]
    public async Task Submit_rejects_exec_end_before_start()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var bad = Input() with { ExecStart = new DateOnly(2027, 1, 1), ExecEnd = new DateOnly(2026, 1, 1) };
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(bad, default));
    }

    [Fact]
    public async Task Submit_rejects_missing_attachment()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var bad = Input() with { AttachmentFileId = null };
        await Assert.ThrowsAsync<ValidationException>(() => svc.SubmitAsync(bad, default));
    }

    // ── inbox provider ────────────────────────────────────────────────────────
    [Fact]
    public async Task Inbox_surfaces_case_to_three_committee_members_then_moves_to_ceo()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var inbox = new COMMITTEE_REVIEW_V1_InboxProvider(new COMMITTEE_REVIEW_V1_CaseStore(db), NewParallel(db), Directory());
        var c = await svc.SubmitAsync(Input(), default);

        Assert.Single(await inbox.GetPendingAsync(Fred, default), r => r.CaseId == c.Id);
        Assert.Single(await inbox.GetPendingAsync(Gina, default), r => r.CaseId == c.Id);
        Assert.Single(await inbox.GetPendingAsync(Dave, default), r => r.CaseId == c.Id);
        Assert.Empty(await inbox.GetPendingAsync(Alice, default));
        Assert.Single(await inbox.GetMineAsync(Sam, default), r => r.CaseId == c.Id);

        var (finance, procurement, _) = await SlotsOf(db, c.Id);
        await svc.DecideAsync(c.Id, finance, Fred, true, null, default);
        await svc.DecideAsync(c.Id, procurement, Gina, true, null, default);

        // Now pending on CEO; committee members no longer see it.
        Assert.Empty(await inbox.GetPendingAsync(Fred, default));
        Assert.Empty(await inbox.GetPendingAsync(Dave, default));
        Assert.Single(await inbox.GetPendingAsync(Alice, default), r => r.CaseId == c.Id);
    }

    [Fact]
    public async Task Inbox_hides_withdrawn_case_from_committee()
    {
        await using var db = new AppDbContext(_options);
        var (svc, _) = NewService(db);
        var inbox = new COMMITTEE_REVIEW_V1_InboxProvider(new COMMITTEE_REVIEW_V1_CaseStore(db), NewParallel(db), Directory());
        var c = await svc.SubmitAsync(Input(), default);
        await svc.CancelAsync(c.Id, Sam, default);

        // Parallel slots are still technically Open in the primitive, but the
        // provider filters on case status, so a withdrawn case never leaks.
        Assert.Empty(await inbox.GetPendingAsync(Fred, default));
    }

    // ── notification templates ────────────────────────────────────────────────
    [Fact]
    public void Templates_render_subject_and_body_substitutions()
    {
        var submit = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderSubmitCommittee("ERP 採購案", "重大採購", "NT$ 5,000,000", "2026-09-01 ~ 2027-08-31", "/cases/committee_review/x");
        Assert.Contains("ERP 採購案", submit.Subject);
        Assert.Contains("重大採購", submit.Body);
        Assert.Contains("NT$ 5,000,000", submit.Body);

        var ret = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderReturnApplicant("ERP 採購案", "NT$ 5,000,000", "效益不足", "/u");
        Assert.Contains("退回", ret.Subject);
        Assert.Contains("效益不足", ret.Body);

        var ceo = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderCeoAssign("ERP 採購案", "重大採購", "NT$ 5,000,000", "2026-09-01 ~ 2027-08-31", 2, "/u");
        Assert.Contains("裁決", ceo.Subject);
        Assert.Contains("2 / 3", ceo.Body);

        var ok = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderApprovedApplicant("ERP 採購案", "重大採購", "NT$ 5,000,000", "2026-09-01 ~ 2027-08-31", "同意", "/u");
        Assert.Contains("核定", ok.Subject);

        var rej = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderRejectedApplicant("ERP 採購案", "重大採購", "NT$ 5,000,000", "不符政策", "/u");
        Assert.Contains("否決", rej.Subject);
        Assert.Contains("不符政策", rej.Body);

        var resub = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderResubmitCommittee("ERP 採購案", "重大採購", "NT$ 5,000,000", "2026-09-01 ~ 2027-08-31", "已補資料", "/u");
        Assert.Contains("已補資料", resub.Body);
    }
}

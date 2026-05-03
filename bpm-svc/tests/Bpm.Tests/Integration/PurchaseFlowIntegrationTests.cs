using Bpm.Application.Common.Exceptions;
using Bpm.Application.Purchase.Commands;
using Bpm.Application.Purchase.Services;
using Bpm.Domain.States;
using Bpm.Tests.Common;
using FluentAssertions;

namespace Bpm.Tests.Integration;

/// One scenario per spec.testCases[] in sample_specs/purchase_v1.json.
/// Each test mirrors the named test case and asserts:
///   - the PurchaseCase walks the expectedPath through the state machine,
///   - the recorded approvers match expectedApprovers,
///   - notifications fire per expectedNotifications (where the spec specifies counts).
public class PurchaseFlowIntegrationTests
{
    private const string Tenant = "acme";
    private static readonly DateTime FakeNow = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "tc_1: 5000 元辦公耗材 — only manager approves, then purchase exec")]
    public async Task Tc1_small_office_purchase_skips_finance_and_ceo()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit  = new SubmitPurchaseCommandHandler (db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApprovePurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var execute = new ExecutePurchaseCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "全聯辦公用品", "office", 5000m,
            "A4 影印紙 x 50 包\n原子筆 x 100 支", "Q2 季度耗材補充", null), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        c.State.Should().Be(PurchaseState.PendingManagerApproval);
        c.CurrentApproverUserId.Should().Be("u_wang_manager");

        var afterMgr = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterMgr.State.Should().Be(PurchaseState.PendingPurchaseExec);  // amount < 10000 → skip Finance
        afterMgr.ManagerApproverUserId.Should().Be("u_wang_manager");
        afterMgr.FinanceApproverUserId.Should().BeNull();
        afterMgr.CeoApproverUserId.Should().BeNull();

        var afterExec = await execute.Handle(new ExecutePurchaseCommand(
            c.Id, "u_purchase_lead", "PO-2026-0001", new DateOnly(2026, 5, 20), null), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterExec.State.Should().Be(PurchaseState.Completed);
        afterExec.PurchaseExecUserId.Should().Be("u_purchase_lead");

        // expectedNotifications: 1 on_assign (manager) + 1 on_assign (purchase) + 1 on_complete
        sender.Sent.Count(n => n.Trigger == "on_assign").Should().Be(2);
        sender.Sent.Count(n => n.Trigger == "on_complete").Should().Be(1);
    }

    [Fact(DisplayName = "tc_2: 50000 元 IT 設備 — manager + finance, no CEO")]
    public async Task Tc2_mid_amount_needs_finance_but_not_ceo()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit  = new SubmitPurchaseCommandHandler (db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApprovePurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var execute = new ExecutePurchaseCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "聯強國際", "it", 50000m,
            "MacBook Air M3 13\" x 1", "新進工程師配機", "quote_50k.pdf"), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var afterMgr = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterMgr.State.Should().Be(PurchaseState.PendingFinanceApproval);
        afterMgr.CurrentApproverUserId.Should().Be("u_finance_lead");

        var afterFin = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_finance_lead"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterFin.State.Should().Be(PurchaseState.PendingPurchaseExec);  // 50k < 100k → skip CEO
        afterFin.FinanceApproverUserId.Should().Be("u_finance_lead");
        afterFin.CeoApproverUserId.Should().BeNull();

        await execute.Handle(new ExecutePurchaseCommand(
            c.Id, "u_purchase_lead", "PO-2026-0002", new DateOnly(2026, 5, 25), "rush"), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var final = await db.Context.PurchaseCases.FindAsync(c.Id);
        final!.State.Should().Be(PurchaseState.Completed);
    }

    [Fact(DisplayName = "tc_3: 200000 元服務委外 — manager + finance + CEO")]
    public async Task Tc3_large_amount_needs_full_chain()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit  = new SubmitPurchaseCommandHandler (db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApprovePurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var execute = new ExecutePurchaseCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "資安顧問公司", "service", 200000m,
            "年度資安滲透測試", "ISO 27001 稽核要求", "quote_200k.pdf"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        c.State.Should().Be(PurchaseState.PendingManagerApproval);

        var afterMgr = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterMgr.State.Should().Be(PurchaseState.PendingFinanceApproval);

        var afterFin = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_finance_lead"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterFin.State.Should().Be(PurchaseState.PendingCeoApproval);
        afterFin.CurrentApproverUserId.Should().Be("u_ceo");

        var afterCeo = await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_ceo"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterCeo.State.Should().Be(PurchaseState.PendingPurchaseExec);
        afterCeo.CeoApproverUserId.Should().Be("u_ceo");

        await execute.Handle(new ExecutePurchaseCommand(
            c.Id, "u_purchase_lead", "PO-2026-0003", new DateOnly(2026, 6, 1), null), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var final = await db.Context.PurchaseCases.FindAsync(c.Id);
        final!.State.Should().Be(PurchaseState.Completed);
    }

    [Fact(DisplayName = "tc_4: 10000 元 unattached quote — validator returns 400")]
    public async Task Tc4_boundary_quote_required_when_amount_at_threshold()
    {
        // Validator catches it before the handler is even reached.
        var validator = new SubmitPurchaseCommandValidator();

        var cmd = new SubmitPurchaseCommand(
            Tenant, "u_wilson", "邊界測試", "other", 10000m,
            "boundary", "邊界測試 — 沒附 quote_file 預期 400", null);

        var result = await validator.ValidateAsync(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Be("quote_file is required when amount >= 10000");
    }

    [Fact(DisplayName = "tc_4 (lower bound): 9999 unattached quote — validator passes")]
    public async Task Tc4_below_threshold_does_not_require_quote()
    {
        var validator = new SubmitPurchaseCommandValidator();
        var cmd = new SubmitPurchaseCommand(
            Tenant, "u_wilson", "邊界測試 (下界)", "other", 9999m,
            "boundary", "邊界測試 — 9999 不必附", null);

        var result = await validator.ValidateAsync(cmd);
        result.IsValid.Should().BeTrue();
    }

    [Fact(DisplayName = "Reject path: manager rejects → state=Rejected")]
    public async Task Reject_at_manager_records_reason_and_terminates()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit = new SubmitPurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var reject = new RejectPurchaseCommandHandler (db.AsAppDbContext(), clock);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "退回測試", "office", 5000m, "x", "y", null), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var afterReject = await reject.Handle(new RejectPurchaseCommand(c.Id, "u_wang_manager", "預算不足"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterReject.State.Should().Be(PurchaseState.Rejected);
        afterReject.RejectedByUserId.Should().Be("u_wang_manager");
        afterReject.RejectionReason.Should().Be("預算不足");
    }

    [Fact(DisplayName = "Approver mismatch: wrong user attempts approve → 409 ConflictException")]
    public async Task Approver_mismatch_throws_conflict()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit  = new SubmitPurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApprovePurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "錯人測試", "office", 5000m, "x", "y", null), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var act = () => approve.Handle(new ApprovePurchaseCommand(c.Id, "u_finance_lead"), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact(DisplayName = "Execute by non-Purchase role → 409 ConflictException")]
    public async Task Execute_by_non_purchase_role_throws_conflict()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new PurchaseApprovalResolver(identity);
        var emitter = new PurchaseNotificationEmitter(identity, sender);

        var submit  = new SubmitPurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApprovePurchaseCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var execute = new ExecutePurchaseCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitPurchaseCommand(
            Tenant, "u_wilson", "exec test", "office", 5000m, "x", "y", null), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        await approve.Handle(new ApprovePurchaseCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        // u_wang_manager is not in role:Purchase
        var act = () => execute.Handle(new ExecutePurchaseCommand(
            c.Id, "u_wang_manager", "PO-X", new DateOnly(2026, 5, 30), null), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }
}

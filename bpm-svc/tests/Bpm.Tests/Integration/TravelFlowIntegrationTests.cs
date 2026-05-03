using Bpm.Application.Travel.Commands;
using Bpm.Application.Travel.Services;
using Bpm.Domain.States;
using Bpm.Tests.Common;
using FluentAssertions;

namespace Bpm.Tests.Integration;

/// One scenario per spec.testCases[] in sample_specs/travel_v1.json.
public class TravelFlowIntegrationTests
{
    private const string Tenant = "acme";
    private static readonly DateTime FakeNow = new(2026, 5, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "tc_1: 國內出差 — 主管核准即可")]
    public async Task Tc1_domestic_skip_vp()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new TravelApprovalResolver(identity);
        var emitter = new TravelNotificationEmitter(identity, sender);

        var submit  = new SubmitTravelCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApproveTravelCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var book    = new BookTravelCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitTravelCommand(
            Tenant, "u_wilson", "domestic", "高雄",
            new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12),
            "客戶現場部署", 8000m), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        c.State.Should().Be(TravelState.PendingManagerApproval);
        c.CurrentApproverUserId.Should().Be("u_wang_manager");

        var afterMgr = await approve.Handle(new ApproveTravelCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterMgr.State.Should().Be(TravelState.PendingAdminBook);
        afterMgr.VpApproverUserId.Should().BeNull();

        var afterBook = await book.Handle(new BookTravelCommand(
            c.Id, "u_admin_lead", "TPE-2026-0001", null, null), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterBook.State.Should().Be(TravelState.Completed);
        afterBook.TicketRef.Should().Be("TPE-2026-0001");

        // expectedNotifications: 1 on_assign (manager) + 1 on_assign (admin) + 1 on_complete
        sender.Sent.Count(n => n.Trigger == "on_assign").Should().Be(2);
        sender.Sent.Count(n => n.Trigger == "on_complete").Should().Be(1);
    }

    [Fact(DisplayName = "tc_2: 國外出差 — 主管 + 副總")]
    public async Task Tc2_international_needs_vp()
    {
        await using var db = await TestDb.CreateAsync();
        var clock = new StubClock(FakeNow);
        var identity = TestEmployees.Default();
        var sender = new CapturingNotificationSender();
        var resolver = new TravelApprovalResolver(identity);
        var emitter = new TravelNotificationEmitter(identity, sender);

        var submit  = new SubmitTravelCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var approve = new ApproveTravelCommandHandler(db.AsAppDbContext(), clock, resolver, emitter);
        var book    = new BookTravelCommandHandler(db.AsAppDbContext(), clock, identity, emitter);

        var c = await submit.Handle(new SubmitTravelCommand(
            Tenant, "u_wilson", "international", "東京 / Japan",
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5),
            "客戶 kickoff", 80000m), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var afterMgr = await approve.Handle(new ApproveTravelCommand(c.Id, "u_wang_manager"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterMgr.State.Should().Be(TravelState.PendingVpApproval);
        afterMgr.CurrentApproverUserId.Should().Be("u_chen_vp");

        var afterVp = await approve.Handle(new ApproveTravelCommand(c.Id, "u_chen_vp"), CancellationToken.None);
        await db.Context.SaveChangesAsync();
        afterVp.State.Should().Be(TravelState.PendingAdminBook);
        afterVp.VpApproverUserId.Should().Be("u_chen_vp");

        await book.Handle(new BookTravelCommand(c.Id, "u_admin_lead", "JL-2026-0002", "Tokyo Hilton", null), CancellationToken.None);
        await db.Context.SaveChangesAsync();

        var final = await db.Context.TravelCases.FindAsync(c.Id);
        final!.State.Should().Be(TravelState.Completed);
    }

    [Fact(DisplayName = "tc_3: 預算超界 — validator 400")]
    public async Task Tc3_over_budget_fails_validation()
    {
        var validator = new SubmitTravelCommandValidator();
        var cmd = new SubmitTravelCommand(
            Tenant, "u_wilson", "international", "Test",
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2),
            "boundary", 1_000_001m);

        var result = await validator.ValidateAsync(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "EstimatedCost must satisfy 0 < value <= 1,000,000");
    }
}

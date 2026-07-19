using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.TRQ.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.TRQ.V1;

/// <summary>
/// State machine for TRQ (Travel Request) V1. One transition per public
/// method. All persistence + notification calls happen inside the
/// caller's EF unit-of-work — controllers call
/// <see cref="ITRQ_V1_CaseStore.SaveChangesAsync"/> via the methods below.
///
/// Status graph (mirrors the bundle's BPMN nodes s/req/ap/e):
/// <code>
///   start(s) ─► PendingManager(ap) ──approve──► Completed(e)
///                     │
///                  reject
///                     ▼
///               ResubmitRequired ──resubmit──► PendingManager (again)
/// </code>
/// Approver is <c>submitter.manager</c> (spec approval node <c>ap</c>).
/// </summary>
public sealed class TRQ_V1_TravelRequestService(
    ITRQ_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<TRQ_V1_TravelRequestService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "TRQ";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string TravelType,
        string DepartureCity,
        string DestinationCity,
        DateOnly DepartDate,
        DateOnly? ReturnDate,
        string ChargeTo,
        string TravelPurpose,
        string? PassportName,
        string? SeatPreference,
        string? PickupRequired);

    // ============================================================
    // req.actions[submit]  /  req (resubmit on rejection)
    // ============================================================

    public async Task<TRQ_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval: submitter has no manager assigned, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new TRQ_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            TravelType = input.TravelType.Trim(),
            DepartureCity = input.DepartureCity.Trim(),
            DestinationCity = input.DestinationCity.Trim(),
            DepartDate = input.DepartDate,
            ReturnDate = input.ReturnDate,
            ChargeTo = input.ChargeTo.Trim(),
            TravelPurpose = input.TravelPurpose.Trim(),
            PassportName = input.PassportName?.Trim(),
            SeatPreference = input.SeatPreference?.Trim(),
            PickupRequired = input.PickupRequired?.Trim(),
            Status = TRQ_V1_CaseStatus.PendingManager,
            ManagerUserId = manager,
            CurrentAssigneeUserId = manager,
            RoundCount = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await NotifySubmittedAsync(c, ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    public async Task<TRQ_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != TRQ_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval on resubmit: submitter has no manager, or the manager is the submitter themselves");

        // New round: refresh payload + clear the manager decision so the
        // timeline reflects the latest pass only.
        c.TravelType = input.TravelType.Trim();
        c.DepartureCity = input.DepartureCity.Trim();
        c.DestinationCity = input.DestinationCity.Trim();
        c.DepartDate = input.DepartDate;
        c.ReturnDate = input.ReturnDate;
        c.ChargeTo = input.ChargeTo.Trim();
        c.TravelPurpose = input.TravelPurpose.Trim();
        c.PassportName = input.PassportName?.Trim();
        c.SeatPreference = input.SeatPreference?.Trim();
        c.PickupRequired = input.PickupRequired?.Trim();
        c.ManagerUserId = manager;
        c.ManagerApproved = null;
        c.ManagerComment = null;
        c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = TRQ_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (PendingManager or ResubmitRequired) — once Completed or
    /// already Cancelled it can no longer be withdrawn. Clears the
    /// assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<TRQ_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is TRQ_V1_CaseStatus.Completed or TRQ_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = TRQ_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("TRQ/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    // ============================================================
    // ap.actions[approve / reject]  (approver = submitter.manager)
    // ============================================================

    public Task<TRQ_V1_Case> ApproveByManagerAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<TRQ_V1_Case> RejectByManagerAsync(
        Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<TRQ_V1_Case> ManagerDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != TRQ_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.CurrentAssigneeUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve;
        c.ManagerComment = comment;
        c.ManagerDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            // Reject edge (f4) targets the submit userTask `req` → send-back
            // to the submitter for re-edit, preserving the form values.
            c.Status = TRQ_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("TRQ/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        // Approve edge (f3) targets the end event → case completes.
        c.Status = TRQ_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    // ============================================================
    // Helpers
    // ============================================================

    private async Task<TRQ_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("TRQ_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TravelType))
            throw Invalid(nameof(input.TravelType), "travel type is required");
        if (string.IsNullOrWhiteSpace(input.DepartureCity))
            throw Invalid(nameof(input.DepartureCity), "departure city is required");
        if (string.IsNullOrWhiteSpace(input.DestinationCity))
            throw Invalid(nameof(input.DestinationCity), "destination city is required");
        if (input.DepartDate == default)
            throw Invalid(nameof(input.DepartDate), "depart date is required");
        if (string.IsNullOrWhiteSpace(input.ChargeTo))
            throw Invalid(nameof(input.ChargeTo), "charge-to is required");
        if (string.IsNullOrWhiteSpace(input.TravelPurpose))
            throw Invalid(nameof(input.TravelPurpose), "travel purpose is required");
    }

    /// <summary>
    /// Resolve the submitter's manager. Spec approver is
    /// <c>submitter.manager</c>; no fallback is declared, so a missing
    /// manager / self-as-manager surfaces as a <see cref="ConflictException"/>
    /// on the caller (this returns null → caller maps to the exception).
    /// </summary>
    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;

        // Self-approval guard — a user can't approve their own request.
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(TRQ_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = TRQ_V1_NotificationTemplates.RenderSubmitted(
            caseUrl: $"/cases/trq/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "TRQ_V1.notify_submitted",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyAssignAsync(TRQ_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName
                            ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var rendered = TRQ_V1_NotificationTemplates.RenderAssign(
            applicantName: applicantName,
            summary: BuildSummary(c),
            caseUrl: $"/cases/trq/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "TRQ_V1.notify_n_submit",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(TRQ_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
            ["round"]       = c.RoundCount.ToString(),
        };

    public static string BuildSummary(TRQ_V1_Case c)
        => $"差旅申請 — {c.DepartureCity} → {c.DestinationCity}（{c.DepartDate:yyyy-MM-dd}）";

    private static string ShortIdLabel(Guid id)
        => id.ToString("N").Substring(0, 8);
}

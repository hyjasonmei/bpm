using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.APE.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.APE.V1;

/// <summary>
/// State machine for APE (Cash in Advance) V1. One transition per
/// public method. Status graph (bundle nodes s/ape/ap/e):
/// <code>
///   start(s) ─► PendingManager(ap) ──approve──► Completed(e)
///                     │
///                  reject
///                     ▼
///               ResubmitRequired ──resubmit──► PendingManager (again)
/// </code>
/// Approver = <c>submitter.manager</c> (spec approval node <c>ap</c>).
/// </summary>
public sealed class APE_V1_AdvancePaymentService(
    IAPE_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<APE_V1_AdvancePaymentService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "APE";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        DateOnly ExpectReceiveDate,
        DateOnly DeductReturnDate,
        string ChargeDepartment,
        string? RechargeOutside,
        string Description,
        decimal Amount,
        string Currency);

    public async Task<APE_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval: submitter has no manager assigned, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new APE_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            ExpectReceiveDate = input.ExpectReceiveDate,
            DeductReturnDate = input.DeductReturnDate,
            ChargeDepartment = input.ChargeDepartment.Trim(),
            RechargeOutside = input.RechargeOutside?.Trim(),
            Description = input.Description.Trim(),
            Amount = input.Amount,
            Currency = input.Currency.Trim(),
            Status = APE_V1_CaseStatus.PendingManager,
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

    public async Task<APE_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != APE_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);

        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException(
                "cannot route approval on resubmit: submitter has no manager, or the manager is the submitter themselves");

        c.ExpectReceiveDate = input.ExpectReceiveDate;
        c.DeductReturnDate = input.DeductReturnDate;
        c.ChargeDepartment = input.ChargeDepartment.Trim();
        c.RechargeOutside = input.RechargeOutside?.Trim();
        c.Description = input.Description.Trim();
        c.Amount = input.Amount;
        c.Currency = input.Currency.Trim();
        c.ManagerUserId = manager;
        c.ManagerApproved = null;
        c.ManagerComment = null;
        c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = APE_V1_CaseStatus.PendingManager;
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
    public async Task<APE_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is APE_V1_CaseStatus.Completed or APE_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = APE_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("APE/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    public Task<APE_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: true, comment, ct);

    public Task<APE_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, approve: false, comment, ct);

    private async Task<APE_V1_Case> ManagerDecisionAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != APE_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve;
        c.ManagerComment = comment;
        c.ManagerDecisionAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = APE_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("APE/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = APE_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        return c;
    }

    private async Task<APE_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct)
           ?? throw new NotFoundException("APE_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (input.ExpectReceiveDate == default)
            throw Invalid(nameof(input.ExpectReceiveDate), "expected receive date is required");
        if (input.DeductReturnDate == default)
            throw Invalid(nameof(input.DeductReturnDate), "deduct / return date is required");
        if (string.IsNullOrWhiteSpace(input.ChargeDepartment))
            throw Invalid(nameof(input.ChargeDepartment), "charge department is required");
        if (string.IsNullOrWhiteSpace(input.Description))
            throw Invalid(nameof(input.Description), "description is required");
        if (input.Amount <= 0)
            throw Invalid(nameof(input.Amount), "amount must be greater than zero");
        if (string.IsNullOrWhiteSpace(input.Currency))
            throw Invalid(nameof(input.Currency), "currency is required");
    }

    /// <summary>Resolve submitter.manager; null on missing / self.</summary>
    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(APE_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var rendered = APE_V1_NotificationTemplates.RenderSubmitted($"/cases/ape/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "APE_V1.notify_submitted",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private async Task NotifyAssignAsync(APE_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicantName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var rendered = APE_V1_NotificationTemplates.RenderAssign(applicantName, BuildSummary(c), $"/cases/ape/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage(
            SourceId:   "APE_V1.notify_n_submit",
            Subject:    rendered.Subject,
            Body:       rendered.Body,
            Channels:   new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            Context:    NotificationContext(c)
        ), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(APE_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"]      = c.Id.ToString(),
            ["flowCode"]    = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
            ["stage"]       = c.Status.ToString(),
            ["round"]       = c.RoundCount.ToString(),
        };

    public static string BuildSummary(APE_V1_Case c)
        => $"預支現金 — {c.Currency} {c.Amount:N0}（{c.ChargeDepartment}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.ETM.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.ETM.V1;

/// <summary>
/// State machine for ETM (Employee Termination) V1.
/// <code>
///   start ─► PendingManager ──approve──► PendingHandover ──complete──► Completed
///                │
///             reject ─► ResubmitRequired ──resubmit──► PendingManager
/// </code>
/// Approver = submitter.manager (node <c>ap</c>); handover (node
/// <c>ho</c>) completed by the original submitter.
/// </summary>
public sealed class ETM_V1_TerminationService(
    IETM_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<ETM_V1_TerminationService> log,
    INotifyDispatcher notify,
    IActorAuthorizer auth)
{
    public const string FlowCode = "ETM";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string EmployeeName,
        string EmployeeId,
        DateOnly LastWorkingDate,
        string Reason,
        string? ProvideCertificate);

    public sealed record HandoverInput(
        string? OutstandingPayment,
        IReadOnlyList<ETM_V1_ReturnItem> ReturnItems);

    public async Task<ETM_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval: submitter has no manager, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new ETM_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            EmployeeName = input.EmployeeName.Trim(),
            EmployeeId = input.EmployeeId.Trim(),
            LastWorkingDate = input.LastWorkingDate,
            Reason = input.Reason.Trim(),
            ProvideCertificate = input.ProvideCertificate?.Trim(),
            Status = ETM_V1_CaseStatus.PendingManager,
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

    public async Task<ETM_V1_Case> ResubmitAsync(Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != ETM_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval on resubmit: submitter has no manager");

        c.EmployeeName = input.EmployeeName.Trim();
        c.EmployeeId = input.EmployeeId.Trim();
        c.LastWorkingDate = input.LastWorkingDate;
        c.Reason = input.Reason.Trim();
        c.ProvideCertificate = input.ProvideCertificate?.Trim();
        c.ManagerUserId = manager;
        c.ManagerApproved = null; c.ManagerComment = null; c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = ETM_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    /// <summary>
    /// Submitter withdraws their own case. Allowed in any non-terminal
    /// state (PendingManager, PendingHandover, or ResubmitRequired) — once
    /// Completed or already Cancelled it can no longer be withdrawn. Clears
    /// the assignee so the case drops out of every inbox.
    /// </summary>
    public async Task<ETM_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may withdraw this case");
        if (c.Status is ETM_V1_CaseStatus.Completed or ETM_V1_CaseStatus.Cancelled)
            throw new ConflictException($"case is in status {c.Status} and can no longer be withdrawn");

        c.Status = ETM_V1_CaseStatus.Cancelled;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("ETM/{CaseId}: withdrawn by submitter", c.Id);
        return c;
    }

    public Task<ETM_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<ETM_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<ETM_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != ETM_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId is not { } mgr || !await auth.CanActAsync(mgr, actorUserId, ct))
            throw new ForbiddenException("only the assigned manager (or their active delegate) may act on this case");

        c.ManagerApproved = approve; c.ManagerComment = comment; c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = ETM_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("ETM/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = ETM_V1_CaseStatus.PendingHandover;
        c.CurrentAssigneeUserId = c.SubmitterUserId;
        await store.SaveChangesAsync(ct);
        await NotifyHandoverAsync(c, c.SubmitterUserId, ct);
        return c;
    }

    public async Task<ETM_V1_Case> CompleteHandoverAsync(
        Guid caseId, Guid actorUserId, HandoverInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != ETM_V1_CaseStatus.PendingHandover)
            throw new ConflictException($"case is in status {c.Status}, expected PendingHandover");
        if (c.CurrentAssigneeUserId is not { } assignee || !await auth.CanActAsync(assignee, actorUserId, ct))
            throw new ForbiddenException("only the assigned coordinator (or their active delegate) may complete handover");
        if (input.ReturnItems.Count < 1)
            throw Invalid(nameof(input.ReturnItems), "at least one handover item is required");

        c.OutstandingPayment = input.OutstandingPayment?.Trim();
        c.ReturnItems = input.ReturnItems.ToList();
        c.HandoverByUserId = actorUserId;
        c.HandoverAt = clock.UtcNow;
        c.Status = ETM_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("ETM/{CaseId}: handover complete ({N} items); terminated", c.Id, c.ReturnItems.Count);
        return c;
    }

    private async Task<ETM_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct) ?? throw new NotFoundException("ETM_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.EmployeeName)) throw Invalid(nameof(input.EmployeeName), "employee name is required");
        if (string.IsNullOrWhiteSpace(input.EmployeeId)) throw Invalid(nameof(input.EmployeeId), "employee id is required");
        if (input.LastWorkingDate == default) throw Invalid(nameof(input.LastWorkingDate), "last working date is required");
        if (string.IsNullOrWhiteSpace(input.Reason)) throw Invalid(nameof(input.Reason), "reason is required");
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(ETM_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = ETM_V1_NotificationTemplates.RenderSubmitted($"/cases/etm/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("ETM_V1.notify_submitted", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyAssignAsync(ETM_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var r = ETM_V1_NotificationTemplates.RenderAssign(applicant, BuildSummary(c), $"/cases/etm/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("ETM_V1.notify_assign", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyHandoverAsync(ETM_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var recipient = await directory.GetByIdAsync(recipientUserId, ct);
        var r = ETM_V1_NotificationTemplates.RenderHandover(BuildSummary(c), $"/cases/etm/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("ETM_V1.notify_handover", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(ETM_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode, ["flowVersion"] = FlowVersion.ToString(),
            ["stage"] = c.Status.ToString(), ["round"] = c.RoundCount.ToString(),
        };

    public static string BuildSummary(ETM_V1_Case c)
        => $"員工離職 — {c.EmployeeName}（{c.Reason}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

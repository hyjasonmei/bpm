using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.EOB.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.EOB.V1;

/// <summary>
/// State machine for EOB (Employee Onboarding) V1.
/// <code>
///   start ─► PendingManager ──approve──► PendingSetup ──complete──► Completed
///                │
///             reject ─► ResubmitRequired ──resubmit──► PendingManager
/// </code>
/// Approver = submitter.manager (node <c>ap</c>); basic setup (node
/// <c>su</c>) completed by the original submitter.
/// </summary>
public sealed class EOB_V1_OnboardingService(
    IEOB_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<EOB_V1_OnboardingService> log,
    INotifyDispatcher notify)
{
    public const string FlowCode = "EOB";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string FirstName,
        string LastName,
        string BusinessTitle,
        string EmployeeLocation,
        DateOnly OnboardDate,
        string? RequireMailbox,
        string CostCenter,
        string? ContractNumber,
        DateOnly? ContractEffectiveDate,
        DateOnly? ContractExpirationDate);

    public async Task<EOB_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval: submitter has no manager, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new EOB_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            FirstName = input.FirstName.Trim(),
            LastName = input.LastName.Trim(),
            BusinessTitle = input.BusinessTitle.Trim(),
            EmployeeLocation = input.EmployeeLocation.Trim(),
            OnboardDate = input.OnboardDate,
            RequireMailbox = input.RequireMailbox?.Trim(),
            CostCenter = input.CostCenter.Trim(),
            ContractNumber = input.ContractNumber?.Trim(),
            ContractEffectiveDate = input.ContractEffectiveDate,
            ContractExpirationDate = input.ContractExpirationDate,
            Status = EOB_V1_CaseStatus.PendingManager,
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

    public async Task<EOB_V1_Case> ResubmitAsync(Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != EOB_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval on resubmit: submitter has no manager");

        c.FirstName = input.FirstName.Trim();
        c.LastName = input.LastName.Trim();
        c.BusinessTitle = input.BusinessTitle.Trim();
        c.EmployeeLocation = input.EmployeeLocation.Trim();
        c.OnboardDate = input.OnboardDate;
        c.RequireMailbox = input.RequireMailbox?.Trim();
        c.CostCenter = input.CostCenter.Trim();
        c.ContractNumber = input.ContractNumber?.Trim();
        c.ContractEffectiveDate = input.ContractEffectiveDate;
        c.ContractExpirationDate = input.ContractExpirationDate;
        c.ManagerUserId = manager;
        c.ManagerApproved = null; c.ManagerComment = null; c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = EOB_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    public Task<EOB_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<EOB_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<EOB_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != EOB_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId != actorUserId)
            throw new ForbiddenException("only the assigned manager may act on this case");

        c.ManagerApproved = approve; c.ManagerComment = comment; c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = EOB_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("EOB/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = EOB_V1_CaseStatus.PendingSetup;
        c.CurrentAssigneeUserId = c.SubmitterUserId;
        await store.SaveChangesAsync(ct);
        await NotifySetupAsync(c, c.SubmitterUserId, ct);
        return c;
    }

    public async Task<EOB_V1_Case> CompleteSetupAsync(
        Guid caseId, Guid actorUserId, IReadOnlyList<EOB_V1_SetupTask> tasks, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != EOB_V1_CaseStatus.PendingSetup)
            throw new ConflictException($"case is in status {c.Status}, expected PendingSetup");
        if (c.CurrentAssigneeUserId != actorUserId)
            throw new ForbiddenException("only the assigned coordinator may complete setup");
        if (tasks.Count < 1)
            throw Invalid(nameof(tasks), "at least one setup task is required");

        c.SetupTasks = tasks.ToList();
        c.SetupByUserId = actorUserId;
        c.SetupAt = clock.UtcNow;
        c.Status = EOB_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("EOB/{CaseId}: setup complete ({N} tasks); onboarded", c.Id, c.SetupTasks.Count);
        return c;
    }

    private async Task<EOB_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct) ?? throw new NotFoundException("EOB_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FirstName)) throw Invalid(nameof(input.FirstName), "first name is required");
        if (string.IsNullOrWhiteSpace(input.LastName)) throw Invalid(nameof(input.LastName), "last name is required");
        if (string.IsNullOrWhiteSpace(input.BusinessTitle)) throw Invalid(nameof(input.BusinessTitle), "business title is required");
        if (string.IsNullOrWhiteSpace(input.EmployeeLocation)) throw Invalid(nameof(input.EmployeeLocation), "location is required");
        if (input.OnboardDate == default) throw Invalid(nameof(input.OnboardDate), "onboard date is required");
        if (string.IsNullOrWhiteSpace(input.CostCenter)) throw Invalid(nameof(input.CostCenter), "cost center is required");
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(EOB_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = EOB_V1_NotificationTemplates.RenderSubmitted($"/cases/eob/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("EOB_V1.notify_submitted", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyAssignAsync(EOB_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var r = EOB_V1_NotificationTemplates.RenderAssign(applicant, BuildSummary(c), $"/cases/eob/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("EOB_V1.notify_assign", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifySetupAsync(EOB_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var recipient = await directory.GetByIdAsync(recipientUserId, ct);
        var r = EOB_V1_NotificationTemplates.RenderSetup(BuildSummary(c), $"/cases/eob/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("EOB_V1.notify_setup", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(EOB_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode, ["flowVersion"] = FlowVersion.ToString(),
            ["stage"] = c.Status.ToString(), ["round"] = c.RoundCount.ToString(),
        };

    public static string BuildSummary(EOB_V1_Case c)
        => $"新進員工 — {c.FirstName} {c.LastName}（{c.BusinessTitle}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.FAD.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.FAD.V1;

/// <summary>
/// State machine for FAD (Fixed Asset Disposal) V1.
/// <code>
///   start ─► PendingManager(IT judge) ──approve──► PendingConfirm ──complete──► Completed
///                │
///             reject ─► ResubmitRequired ──resubmit──► PendingManager
/// </code>
/// Judgment approver = <c>submitter.manager</c> (node <c>ap</c>);
/// confirmation (node <c>cf</c>) by the original submitter.
/// </summary>
public sealed class FAD_V1_DisposalService(
    IFAD_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<FAD_V1_DisposalService> log,
    INotifyDispatcher notify)
{
    public const string FlowCode = "FAD";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        string DisposalReason,
        string AssetId,
        string AssetName,
        string? Description,
        Guid? PhotoFileId);

    public async Task<FAD_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route judgment: submitter has no manager, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new FAD_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            DisposalReason = input.DisposalReason.Trim(),
            AssetId = input.AssetId.Trim(),
            AssetName = input.AssetName.Trim(),
            Description = input.Description?.Trim(),
            PhotoFileId = input.PhotoFileId,
            Status = FAD_V1_CaseStatus.PendingManager,
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

    public async Task<FAD_V1_Case> ResubmitAsync(Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAD_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route judgment on resubmit: submitter has no manager");

        c.DisposalReason = input.DisposalReason.Trim();
        c.AssetId = input.AssetId.Trim();
        c.AssetName = input.AssetName.Trim();
        c.Description = input.Description?.Trim();
        c.PhotoFileId = input.PhotoFileId;
        c.ManagerUserId = manager;
        c.ManagerApproved = null; c.ManagerComment = null; c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = FAD_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    public Task<FAD_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<FAD_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<FAD_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAD_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId != actorUserId)
            throw new ForbiddenException("only the assigned judge may act on this case");

        c.ManagerApproved = approve; c.ManagerComment = comment; c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = FAD_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("FAD/{CaseId}: judgment rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        c.Status = FAD_V1_CaseStatus.PendingConfirm;
        c.CurrentAssigneeUserId = c.SubmitterUserId;   // confirmation by requester
        await store.SaveChangesAsync(ct);
        await NotifyConfirmAsync(c, c.SubmitterUserId, ct);
        return c;
    }

    public async Task<FAD_V1_Case> CompleteConfirmAsync(
        Guid caseId, Guid actorUserId, string handlingResult, string? remark, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAD_V1_CaseStatus.PendingConfirm)
            throw new ConflictException($"case is in status {c.Status}, expected PendingConfirm");
        if (c.CurrentAssigneeUserId != actorUserId)
            throw new ForbiddenException("only the assigned confirmer may complete this case");
        if (string.IsNullOrWhiteSpace(handlingResult))
            throw Invalid(nameof(handlingResult), "handling result is required");

        c.ConfirmedByUserId = actorUserId;
        c.HandlingResult = handlingResult.Trim();
        c.ConfirmRemark = remark;
        c.ConfirmedAt = clock.UtcNow;
        c.Status = FAD_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("FAD/{CaseId}: confirmed ({Result}); disposed", c.Id, c.HandlingResult);
        return c;
    }

    private async Task<FAD_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct) ?? throw new NotFoundException("FAD_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.DisposalReason))
            throw Invalid(nameof(input.DisposalReason), "disposal reason is required");
        if (string.IsNullOrWhiteSpace(input.AssetId))
            throw Invalid(nameof(input.AssetId), "asset id is required");
        if (string.IsNullOrWhiteSpace(input.AssetName))
            throw Invalid(nameof(input.AssetName), "asset name is required");
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(FAD_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = FAD_V1_NotificationTemplates.RenderSubmitted($"/cases/fad/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAD_V1.notify_submitted", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyAssignAsync(FAD_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var r = FAD_V1_NotificationTemplates.RenderAssign(applicant, BuildSummary(c), $"/cases/fad/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAD_V1.notify_assign", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyConfirmAsync(FAD_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var recipient = await directory.GetByIdAsync(recipientUserId, ct);
        var r = FAD_V1_NotificationTemplates.RenderConfirm(BuildSummary(c), $"/cases/fad/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAD_V1.notify_confirm", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(FAD_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode, ["flowVersion"] = FlowVersion.ToString(),
            ["stage"] = c.Status.ToString(), ["round"] = c.RoundCount.ToString(),
        };

    public static string BuildSummary(FAD_V1_Case c)
        => $"資產處份 — {c.AssetName}（{c.DisposalReason}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

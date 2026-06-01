using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Domain.Features.FAP.V1;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Features.FAP.V1;

/// <summary>
/// State machine for FAP (Fixed Asset Purchase) V1.
/// <code>
///   start ─► PendingManager ──approve──► [issue PO] ─► PendingVerification ──complete──► Completed
///                │
///             reject ─► ResubmitRequired ──resubmit──► PendingManager
/// </code>
/// Stage 1 = submitter.manager (node <c>ap</c>). The PO serviceTask
/// (node <c>po</c>) is a no-op marker in the POC — no integration in the
/// bundle — so manager-approve issues a PO number inline then routes to
/// verification (node <c>vf</c>), performed by the original submitter
/// (goods receipt).
/// </summary>
public sealed class FAP_V1_PurchaseService(
    IFAP_V1_CaseStore store,
    IOrgChartReader org,
    IPrincipalDirectory directory,
    IClock clock,
    ILogger<FAP_V1_PurchaseService> log,
    INotifyDispatcher notify)
{
    public const string FlowCode = "FAP";
    public const int FlowVersion = 1;

    public sealed record SubmitInput(
        Guid SubmitterUserId,
        IReadOnlyList<FAP_V1_PurchaseItem> PurchaseItems,
        string ShippingLocation,
        string ChargeTo,
        string Purpose,
        DateOnly? ExpectedDate,
        string? Note);

    public async Task<FAP_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval: submitter has no manager, or the manager is the submitter themselves");

        var now = clock.UtcNow;
        var c = new FAP_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            PurchaseItems = input.PurchaseItems.ToList(),
            ShippingLocation = input.ShippingLocation.Trim(),
            ChargeTo = input.ChargeTo.Trim(),
            Purpose = input.Purpose.Trim(),
            ExpectedDate = input.ExpectedDate,
            Note = input.Note?.Trim(),
            Status = FAP_V1_CaseStatus.PendingManager,
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

    public async Task<FAP_V1_Case> ResubmitAsync(Guid caseId, Guid actorUserId, SubmitInput input, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAP_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException($"case is in status {c.Status}, expected ResubmitRequired");
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("only the original submitter may resubmit this case");

        ValidateSubmitPayload(input);
        var manager = await ResolveManagerAsync(input.SubmitterUserId, ct);
        if (manager is null)
            throw new ConflictException("cannot route approval on resubmit: submitter has no manager");

        c.PurchaseItems = input.PurchaseItems.ToList();
        c.ShippingLocation = input.ShippingLocation.Trim();
        c.ChargeTo = input.ChargeTo.Trim();
        c.Purpose = input.Purpose.Trim();
        c.ExpectedDate = input.ExpectedDate;
        c.Note = input.Note?.Trim();
        c.ManagerUserId = manager;
        c.ManagerApproved = null; c.ManagerComment = null; c.ManagerDecisionAt = null;
        c.CurrentAssigneeUserId = manager;
        c.Status = FAP_V1_CaseStatus.PendingManager;
        c.RoundCount += 1;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        await NotifyAssignAsync(c, manager.Value, ct);
        return c;
    }

    public Task<FAP_V1_Case> ApproveByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, true, comment, ct);
    public Task<FAP_V1_Case> RejectByManagerAsync(Guid caseId, Guid actorUserId, string? comment, CancellationToken ct)
        => ManagerDecisionAsync(caseId, actorUserId, false, comment, ct);

    private async Task<FAP_V1_Case> ManagerDecisionAsync(Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAP_V1_CaseStatus.PendingManager)
            throw new ConflictException($"case is in status {c.Status}, expected PendingManager");
        if (c.ManagerUserId != actorUserId)
            throw new ForbiddenException("only the assigned manager may act on this case");

        c.ManagerApproved = approve; c.ManagerComment = comment; c.ManagerDecisionAt = clock.UtcNow; c.LastActivityAt = clock.UtcNow;

        if (!approve)
        {
            c.Status = FAP_V1_CaseStatus.ResubmitRequired;
            c.CurrentAssigneeUserId = c.SubmitterUserId;
            await store.SaveChangesAsync(ct);
            log.LogInformation("FAP/{CaseId}: manager rejected (round {Round})", c.Id, c.RoundCount);
            await NotifyAssignAsync(c, c.SubmitterUserId, ct);
            return c;
        }

        // serviceTask `po` — issue PO (no-op marker in POC), then route to verification.
        c.PurchaseOrderNo = $"PO-{c.Id.ToString("N")[..8].ToUpperInvariant()}";
        c.PoIssuedAt = clock.UtcNow;
        c.Status = FAP_V1_CaseStatus.PendingVerification;
        c.CurrentAssigneeUserId = c.SubmitterUserId;   // goods receipt by requester
        await store.SaveChangesAsync(ct);
        log.LogInformation("FAP/{CaseId}: approved, PO {Po} issued, routed to verification", c.Id, c.PurchaseOrderNo);
        await NotifyVerifyAsync(c, c.SubmitterUserId, ct);
        return c;
    }

    public async Task<FAP_V1_Case> CompleteVerificationAsync(
        Guid caseId, Guid actorUserId, string received, string? remark, CancellationToken ct)
    {
        var c = await LoadAsync(caseId, ct);
        if (c.Status != FAP_V1_CaseStatus.PendingVerification)
            throw new ConflictException($"case is in status {c.Status}, expected PendingVerification");
        if (c.CurrentAssigneeUserId != actorUserId)
            throw new ForbiddenException("only the assigned verifier may complete verification");
        if (string.IsNullOrWhiteSpace(received))
            throw Invalid(nameof(received), "verification result is required");

        c.VerifiedByUserId = actorUserId;
        c.Received = received.Trim();
        c.VerificationRemark = remark;
        c.VerifiedAt = clock.UtcNow;
        c.Status = FAP_V1_CaseStatus.Completed;
        c.CurrentAssigneeUserId = null;
        c.CompletedAt = clock.UtcNow;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);
        log.LogInformation("FAP/{CaseId}: verification complete ({Received}); booked into asset list", c.Id, c.Received);
        return c;
    }

    private async Task<FAP_V1_Case> LoadAsync(Guid caseId, CancellationToken ct)
        => await store.FindByIdAsync(caseId, ct) ?? throw new NotFoundException("FAP_V1_Case", caseId);

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private static void ValidateSubmitPayload(SubmitInput input)
    {
        if (input.PurchaseItems.Count < 1)
            throw Invalid(nameof(input.PurchaseItems), "at least one purchase item is required");
        for (var i = 0; i < input.PurchaseItems.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(input.PurchaseItems[i].ItemSpec))
                throw Invalid($"purchaseItems[{i}].itemSpec", "item / spec is required");
            if (input.PurchaseItems[i].Qty <= 0)
                throw Invalid($"purchaseItems[{i}].qty", "qty must be greater than zero");
        }
        if (string.IsNullOrWhiteSpace(input.ShippingLocation))
            throw Invalid(nameof(input.ShippingLocation), "shipping location is required");
        if (string.IsNullOrWhiteSpace(input.ChargeTo))
            throw Invalid(nameof(input.ChargeTo), "charge-to is required");
        if (string.IsNullOrWhiteSpace(input.Purpose))
            throw Invalid(nameof(input.Purpose), "purpose is required");
    }

    public async Task<Guid?> ResolveManagerAsync(Guid submitterUserId, CancellationToken ct)
    {
        var managerId = await org.GetManagerIdAsync(submitterUserId, ct);
        if (managerId is null) return null;
        if (managerId == submitterUserId) return null;
        return managerId;
    }

    private async Task NotifySubmittedAsync(FAP_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = FAP_V1_NotificationTemplates.RenderSubmitted($"/cases/fap/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAP_V1.notify_submitted", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyAssignAsync(FAP_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var lookups = await directory.GetManyAsync(new[] { c.SubmitterUserId, recipientUserId }, ct);
        var applicant = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? ShortIdLabel(c.SubmitterUserId);
        var recipient = lookups.GetValueOrDefault(recipientUserId);
        var r = FAP_V1_NotificationTemplates.RenderAssign(applicant, BuildSummary(c), $"/cases/fap/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAP_V1.notify_assign", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private async Task NotifyVerifyAsync(FAP_V1_Case c, Guid recipientUserId, CancellationToken ct)
    {
        var recipient = await directory.GetByIdAsync(recipientUserId, ct);
        var r = FAP_V1_NotificationTemplates.RenderVerify(BuildSummary(c), $"/cases/fap/{c.Id}");
        await notify.DispatchAsync(new NotifyMessage("FAP_V1.notify_verify", r.Subject, r.Body,
            new[] { "email", "in_app" },
            new[] { new NotifyRecipient(recipientUserId, recipient?.Email, recipient?.DisplayName) },
            NotificationContext(c)), ct);
    }

    private static IReadOnlyDictionary<string, string?> NotificationContext(FAP_V1_Case c)
        => new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(), ["flowCode"] = FlowCode, ["flowVersion"] = FlowVersion.ToString(),
            ["stage"] = c.Status.ToString(), ["round"] = c.RoundCount.ToString(),
        };

    public static string BuildSummary(FAP_V1_Case c)
        => $"資產採購 — {c.PurchaseItems.Count} 項（{c.Purpose}）";

    private static string ShortIdLabel(Guid id) => id.ToString("N").Substring(0, 8);
}

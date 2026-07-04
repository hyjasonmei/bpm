using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.CONTRACT_REVIEW.V1;
using Bpm.Domain.Parallel;
using FluentValidation.Results;

namespace Bpm.Application.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// State machine for CONTRACT_REVIEW V1 (合約審查).
///
/// Submit opens a parallel review gateway (LEGAL + FINANCE 並簽, threshold 2/2 =
/// AND) via the shared <see cref="IParallelApprovalService"/> primitive. When the
/// group resolves:
///   • both approve → PendingLegalManager (single LEGAL_MANAGER 定案歸檔) → Completed;
///   • any reject   → ResubmitRequired — the applicant revises and either
///     re-sends審 (a fresh parallel round, keyed by <see cref="ReviewGatewayKey"/>)
///     or abandons (Cancelled).
/// A submitter may withdraw (撤回) any non-terminal case.
/// </summary>
public sealed class CONTRACT_REVIEW_V1_Service(
    ICONTRACT_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IActorAuthorizer authorizer,
    IClock clock,
    INotifyDispatcher notify,
    IPrincipalDirectory directory)
{
    public const string FlowCode = "CONTRACT_REVIEW";
    public const int FlowVersion = 1;

    // BPMN node ids — MUST match the shipped .bpmn.xml / spec.flow.nodes.
    public const string ForkNodeId = "gateway_fork";
    public const string LegalNodeId = "approval_legal";
    public const string FinanceNodeId = "approval_finance";
    public const string LegalManagerNodeId = "approval_legal_mgr";

    public const string LegalRole = "LEGAL";
    public const string FinanceRole = "FINANCE";
    public const string LegalManagerRole = "LEGAL_MANAGER";

    /// <summary>
    /// Gateway key for the parallel group of a given review round. Round 1 uses
    /// the bare fork node id (so single-round cases read naturally); later rounds
    /// suffix the round so a re-opened group never collides with the resolved one
    /// under <c>GetAsync(caseId, key)</c>.
    /// </summary>
    public static string ReviewGatewayKey(int round) => round <= 1 ? ForkNodeId : $"{ForkNodeId}#r{round}";

    public sealed record SubmitInput(
        Guid SubmitterUserId, string CounterpartyName, string ContractSubject,
        decimal Amount, DateOnly PeriodStart, DateOnly PeriodEnd, Guid? DraftFileId, string? Remarks);

    // ── Submit (task_apply → gateway_fork) ──────────────────────────────────
    public async Task<CONTRACT_REVIEW_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        Validate(input);

        var now = clock.UtcNow;
        var c = new CONTRACT_REVIEW_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            CounterpartyName = input.CounterpartyName.Trim(),
            ContractSubject = input.ContractSubject.Trim(),
            Amount = input.Amount,
            PeriodStart = input.PeriodStart,
            PeriodEnd = input.PeriodEnd,
            DraftFileId = input.DraftFileId,
            Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim(),
            Status = CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview,
            CurrentRound = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await OpenParallelAsync(c, ct);
        await NotifyReviewersAsync(c, resubmit: false, ct);
        return c;
    }

    // ── Parallel decision (approval_legal / approval_finance → gateway_join) ──
    public async Task<CONTRACT_REVIEW_V1_Case> DecideAsync(
        Guid caseId, Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(CONTRACT_REVIEW_V1_Case), caseId);
        if (c.Status != CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview)
            throw new ConflictException("此案件目前不在並簽審查階段");

        // Authorization (assignee-role or delegate) is enforced inside the primitive.
        var result = await parallel.DecideAsync(slotId, actorUserId, approve, comment, ct);

        c.LastActivityAt = clock.UtcNow;
        switch (result.GroupStatus)
        {
            case ParallelGroupStatus.Approved:
                // Both reviews passed → hand off to the LEGAL_MANAGER 定案歸檔 step.
                c.Status = CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager;
                await store.SaveChangesAsync(ct);
                await NotifyLegalManagerAsync(c, ct);
                break;

            case ParallelGroupStatus.Rejected:
                // Any single reject → back to the applicant to revise.
                c.Status = CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired;
                await store.SaveChangesAsync(ct);
                var (rejName, rejReason) = await RejectedSlotInfoAsync(result.Group, comment, ct);
                await NotifyRejectAsync(c, rejName, rejReason, ct);
                break;

            default:
                // Still Open — the other branch hasn't decided yet.
                await store.SaveChangesAsync(ct);
                break;
        }
        return c;
    }

    // ── Revise + re-send審 (task_revise → gateway_fork, 重新送審) ──────────────
    public async Task<CONTRACT_REVIEW_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, string? revisionNote, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(CONTRACT_REVIEW_V1_Case), caseId);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("只有申請人本人可以重新送審此案件");
        if (c.Status != CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException("此案件目前無法重新送審");
        Validate(input);

        c.CounterpartyName = input.CounterpartyName.Trim();
        c.ContractSubject = input.ContractSubject.Trim();
        c.Amount = input.Amount;
        c.PeriodStart = input.PeriodStart;
        c.PeriodEnd = input.PeriodEnd;
        c.DraftFileId = input.DraftFileId;
        c.Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim();
        c.RevisionNote = string.IsNullOrWhiteSpace(revisionNote) ? null : revisionNote.Trim();
        c.CurrentRound += 1;
        c.Status = CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await OpenParallelAsync(c, ct);
        await NotifyReviewersAsync(c, resubmit: true, ct);
        return c;
    }

    // ── Final 定案歸檔 (approval_legal_mgr → end_approved) ─────────────────────
    public async Task<CONTRACT_REVIEW_V1_Case> LegalManagerDecideAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(CONTRACT_REVIEW_V1_Case), caseId);
        if (c.Status != CONTRACT_REVIEW_V1_CaseStatus.PendingLegalManager)
            throw new ConflictException("此案件目前不在法務主管定案階段");
        if (!await authorizer.CanActAsync(null, LegalManagerRole, actorUserId, ct))
            throw new ForbiddenException("只有法務主管（或其代理人）可以定案歸檔");

        var now = clock.UtcNow;
        c.LegalManagerUserId = actorUserId;
        c.LegalManagerApproved = approve;
        c.LegalManagerComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        c.LegalManagerDecisionAt = now;
        c.LastActivityAt = now;

        if (approve)
        {
            c.Status = CONTRACT_REVIEW_V1_CaseStatus.Completed;
            c.CompletedAt = now;
            await store.SaveChangesAsync(ct);
            await NotifyCompletedAsync(c, ct);
        }
        else
        {
            // Spec has a 駁回/退回 action on the archival step but no explicit edge;
            // baked as a send-back into the revise loop (keeps the case alive).
            c.Status = CONTRACT_REVIEW_V1_CaseStatus.ResubmitRequired;
            await store.SaveChangesAsync(ct);
            var name = (await directory.GetByIdAsync(actorUserId, ct))?.DisplayName ?? "法務主管";
            await NotifyRejectAsync(c, name, c.LegalManagerComment ?? "（未填寫退回意見）", ct);
        }
        return c;
    }

    // ── Withdraw / abandon (baseline 撤回 + task_revise 放棄送審) ───────────────
    public async Task<CONTRACT_REVIEW_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(CONTRACT_REVIEW_V1_Case), caseId);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("只有申請人本人可以撤回此案件");
        if (c.Status is CONTRACT_REVIEW_V1_CaseStatus.Completed or CONTRACT_REVIEW_V1_CaseStatus.Cancelled)
            throw new ConflictException("此案件已結案，無法撤回");

        var now = clock.UtcNow;
        c.Status = CONTRACT_REVIEW_V1_CaseStatus.Cancelled;
        c.CompletedAt = now;
        c.LastActivityAt = now;
        await store.SaveChangesAsync(ct);
        // Any still-Open parallel slots are ignored by the inbox (it filters on
        // case status), so no explicit group teardown is required.
        return c;
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private Task OpenParallelAsync(CONTRACT_REVIEW_V1_Case c, CancellationToken ct) =>
        parallel.OpenAsync(FlowCode, FlowVersion, c.Id, ReviewGatewayKey(c.CurrentRound),
            new List<SlotSpec>
            {
                new(LegalNodeId, LegalRole, null),
                new(FinanceNodeId, FinanceRole, null),
            },
            threshold: 2, ct);

    private async Task<(string Name, string Reason)> RejectedSlotInfoAsync(ParallelApprovalGroup group, string? comment, CancellationToken ct)
    {
        var rejected = group.Slots.FirstOrDefault(s => s.Decision == SlotDecision.Rejected);
        var reason = rejected?.Comment ?? comment ?? "（未填寫退回意見）";
        var name = rejected?.DecisionByUserId is { } id
            ? (await directory.GetByIdAsync(id, ct))?.DisplayName ?? "審查者"
            : "審查者";
        return (name, reason);
    }

    private string CaseUrl(CONTRACT_REVIEW_V1_Case c) => $"/cases/contract_review/{c.Id}";
    private static string FmtAmount(decimal amount) => $"NT$ {amount:N0}";

    private IReadOnlyDictionary<string, string?> Ctx(CONTRACT_REVIEW_V1_Case c) =>
        new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(),
            ["flowCode"] = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
        };

    private async Task NotifyReviewersAsync(CONTRACT_REVIEW_V1_Case c, bool resubmit, CancellationToken ct)
    {
        var reviewerIds = new List<Guid>();
        reviewerIds.AddRange(await directory.GetUsersInRoleAsync(LegalRole, ct));
        reviewerIds.AddRange(await directory.GetUsersInRoleAsync(FinanceRole, ct));
        reviewerIds = reviewerIds.Distinct().ToList();
        if (reviewerIds.Count == 0) return;

        var lookups = await directory.GetManyAsync(reviewerIds.Append(c.SubmitterUserId).ToArray(), ct);
        var submitterName = lookups.GetValueOrDefault(c.SubmitterUserId)?.DisplayName ?? c.SubmitterUserId.ToString()[..8];
        var r = resubmit
            ? CONTRACT_REVIEW_V1_NotificationTemplates.RenderResubmitReviewers(
                submitterName, c.CounterpartyName, c.ContractSubject, FmtAmount(c.Amount), c.RevisionNote ?? "—", CaseUrl(c))
            : CONTRACT_REVIEW_V1_NotificationTemplates.RenderSubmitReviewers(
                submitterName, c.CounterpartyName, c.ContractSubject, FmtAmount(c.Amount), CaseUrl(c));

        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_{(resubmit ? "resubmit" : "submit")}_reviewers",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: reviewerIds.Select(id => new NotifyRecipient(
                id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyLegalManagerAsync(CONTRACT_REVIEW_V1_Case c, CancellationToken ct)
    {
        var mgrIds = (await directory.GetUsersInRoleAsync(LegalManagerRole, ct)).Distinct().ToList();
        if (mgrIds.Count == 0) return;
        var lookups = await directory.GetManyAsync(mgrIds, ct);
        var r = CONTRACT_REVIEW_V1_NotificationTemplates.RenderApproveLegalMgr(
            c.CounterpartyName, c.ContractSubject, FmtAmount(c.Amount), CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_approve_legal_mgr",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: mgrIds.Select(id => new NotifyRecipient(
                id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyRejectAsync(CONTRACT_REVIEW_V1_Case c, string approverName, string reason, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = CONTRACT_REVIEW_V1_NotificationTemplates.RenderRejectSubmitter(
            submitter?.DisplayName ?? "申請人", c.CounterpartyName, approverName, reason, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_reject_submitter",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private async Task NotifyCompletedAsync(CONTRACT_REVIEW_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var completedAt = (c.CompletedAt ?? clock.UtcNow).ToString("yyyy-MM-dd HH:mm");
        var r = CONTRACT_REVIEW_V1_NotificationTemplates.RenderCompleteSubmitter(
            submitter?.DisplayName ?? "申請人", c.CounterpartyName, c.ContractSubject, FmtAmount(c.Amount), completedAt, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_complete_submitter",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private static void Validate(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CounterpartyName))
            throw Invalid(nameof(input.CounterpartyName), "對方公司名稱為必填");
        if (string.IsNullOrWhiteSpace(input.ContractSubject))
            throw Invalid(nameof(input.ContractSubject), "合約標的說明為必填");
        if (input.Amount < 0)
            throw Invalid(nameof(input.Amount), "合約金額不得為負數");
        if (input.PeriodEnd < input.PeriodStart)
            throw Invalid(nameof(input.PeriodEnd), "迄日不能早於起日");
        if (input.DraftFileId is null)
            throw Invalid(nameof(input.DraftFileId), "合約草稿檔案為必填");
    }

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}

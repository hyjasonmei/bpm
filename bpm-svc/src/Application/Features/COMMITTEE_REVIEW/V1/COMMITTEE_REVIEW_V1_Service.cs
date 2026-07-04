using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Authorization;
using Bpm.Application.Common.Directory;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Notifications;
using Bpm.Application.Parallel;
using Bpm.Domain.Features.COMMITTEE_REVIEW.V1;
using Bpm.Domain.Parallel;
using FluentValidation.Results;

namespace Bpm.Application.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// State machine for COMMITTEE_REVIEW V1 (委員會審議).
///
/// Submit opens a parallel review gateway (財務 / 採購 / 資訊 三委員並簽, 門檻 2/3 =
/// quorum) via the shared <see cref="IParallelApprovalService"/> primitive. When
/// the group resolves:
///   • ≥2 approve → PendingCeo (執行長最終裁決) → Completed (approve) / Rejected
///     (reject, 終局結案 — NO resubmit);
///   • 任一 reject → ResubmitRequired — the applicant revises and either
///     re-sends審 (a fresh 3-slot parallel round, keyed by <see cref="ReviewGatewayKey"/>)
///     or abandons (Cancelled).
/// A submitter may withdraw (撤回) any non-terminal case.
/// </summary>
public sealed class COMMITTEE_REVIEW_V1_Service(
    ICOMMITTEE_REVIEW_V1_CaseStore store,
    IParallelApprovalService parallel,
    IActorAuthorizer authorizer,
    IClock clock,
    INotifyDispatcher notify,
    IPrincipalDirectory directory)
{
    public const string FlowCode = "COMMITTEE_REVIEW";
    public const int FlowVersion = 1;

    // BPMN node ids — MUST match the shipped .bpmn.xml / spec.flow.nodes.
    public const string ForkNodeId = "gateway_fork";
    public const string FinanceNodeId = "approval_finance";
    public const string ProcurementNodeId = "approval_procurement";
    public const string ItNodeId = "approval_it";
    public const string CeoNodeId = "approval_ceo";

    public const string FinanceRole = "FINANCE";
    public const string ProcurementRole = "PROCUREMENT";
    public const string ItRole = "IT_COMMITTEE";
    public const string CeoRole = "CEO";

    // 門檻 2/3 — quorum: any 2 of the 3 committee members approving passes the gate.
    public const int Quorum = 2;

    /// <summary>
    /// Gateway key for the parallel group of a given review round. Round 1 uses
    /// the bare fork node id (so single-round cases read naturally); later rounds
    /// suffix the round so a re-opened group never collides with the resolved one
    /// under <c>GetAsync(caseId, key)</c>.
    /// </summary>
    public static string ReviewGatewayKey(int round) => round <= 1 ? ForkNodeId : $"{ForkNodeId}#r{round}";

    public sealed record SubmitInput(
        Guid SubmitterUserId, string CaseTitle, string ReviewCategory, decimal ApplyAmount,
        string BenefitDescription, DateOnly ExecStart, DateOnly ExecEnd, Guid? AttachmentFileId, string? Remarks);

    // ── Submit (task_apply → gateway_fork) ──────────────────────────────────
    public async Task<COMMITTEE_REVIEW_V1_Case> SubmitAsync(SubmitInput input, CancellationToken ct)
    {
        Validate(input);

        var now = clock.UtcNow;
        var c = new COMMITTEE_REVIEW_V1_Case
        {
            Id = Guid.NewGuid(),
            SubmitterUserId = input.SubmitterUserId,
            CaseTitle = input.CaseTitle.Trim(),
            ReviewCategory = input.ReviewCategory.Trim(),
            ApplyAmount = input.ApplyAmount,
            BenefitDescription = input.BenefitDescription.Trim(),
            ExecStart = input.ExecStart,
            ExecEnd = input.ExecEnd,
            AttachmentFileId = input.AttachmentFileId,
            Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim(),
            Status = COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview,
            CurrentRound = 1,
            SubmittedAt = now,
            LastActivityAt = now,
        };
        store.Add(c);
        await store.SaveChangesAsync(ct);

        await OpenParallelAsync(c, ct);
        await NotifyCommitteeAsync(c, resubmit: false, ct);
        return c;
    }

    // ── Committee decision (approval_finance/procurement/it → gateway_threshold) ──
    public async Task<COMMITTEE_REVIEW_V1_Case> DecideAsync(
        Guid caseId, Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(COMMITTEE_REVIEW_V1_Case), caseId);
        if (c.Status != COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview)
            throw new ConflictException("此案件目前不在委員並簽審議階段");
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw Invalid(nameof(comment), "退回時請填寫退回意見");

        // Authorization (assignee-role or delegate) is enforced inside the primitive.
        var result = await parallel.DecideAsync(slotId, actorUserId, approve, comment, ct);

        c.LastActivityAt = clock.UtcNow;
        switch (result.GroupStatus)
        {
            case ParallelGroupStatus.Approved:
                // 門檻 2/3 met → hand off to the CEO 最終裁決 step.
                c.Status = COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo;
                await store.SaveChangesAsync(ct);
                var approvedCount = result.Group.Slots.Count(s => s.Decision == SlotDecision.Approved);
                await NotifyCeoAsync(c, approvedCount, ct);
                break;

            case ParallelGroupStatus.Rejected:
                // Any single reject → back to the applicant to revise.
                c.Status = COMMITTEE_REVIEW_V1_CaseStatus.ResubmitRequired;
                await store.SaveChangesAsync(ct);
                var reason = RejectedReason(result.Group, comment);
                await NotifyReturnAsync(c, reason, ct);
                break;

            default:
                // Still Open — the quorum hasn't been reached yet.
                await store.SaveChangesAsync(ct);
                break;
        }
        return c;
    }

    // ── Revise + re-send審 (task_revise → gateway_fork, 重新送審) ──────────────
    public async Task<COMMITTEE_REVIEW_V1_Case> ResubmitAsync(
        Guid caseId, Guid actorUserId, SubmitInput input, string? revisionNote, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(COMMITTEE_REVIEW_V1_Case), caseId);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("只有申請人本人可以重新送審此案件");
        if (c.Status != COMMITTEE_REVIEW_V1_CaseStatus.ResubmitRequired)
            throw new ConflictException("此案件目前無法重新送審");
        Validate(input);
        if (string.IsNullOrWhiteSpace(revisionNote))
            throw Invalid(nameof(revisionNote), "重新送審時請填寫修改說明");

        c.CaseTitle = input.CaseTitle.Trim();
        c.ReviewCategory = input.ReviewCategory.Trim();
        c.ApplyAmount = input.ApplyAmount;
        c.BenefitDescription = input.BenefitDescription.Trim();
        c.ExecStart = input.ExecStart;
        c.ExecEnd = input.ExecEnd;
        c.AttachmentFileId = input.AttachmentFileId;
        c.Remarks = string.IsNullOrWhiteSpace(input.Remarks) ? null : input.Remarks.Trim();
        c.RevisionNote = revisionNote.Trim();
        c.CurrentRound += 1;
        c.Status = COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview;
        c.LastActivityAt = clock.UtcNow;
        await store.SaveChangesAsync(ct);

        await OpenParallelAsync(c, ct);
        await NotifyCommitteeAsync(c, resubmit: true, ct);
        return c;
    }

    // ── CEO 最終裁決 (approval_ceo → end_approved / end_rejected) ───────────────
    public async Task<COMMITTEE_REVIEW_V1_Case> CeoDecideAsync(
        Guid caseId, Guid actorUserId, bool approve, string? comment, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(COMMITTEE_REVIEW_V1_Case), caseId);
        if (c.Status != COMMITTEE_REVIEW_V1_CaseStatus.PendingCeo)
            throw new ConflictException("此案件目前不在執行長裁決階段");
        if (!await authorizer.CanActAsync(null, CeoRole, actorUserId, ct))
            throw new ForbiddenException("只有執行長（或其代理人）可以進行最終裁決");
        if (!approve && string.IsNullOrWhiteSpace(comment))
            throw Invalid(nameof(comment), "否決時請填寫審核意見");

        var now = clock.UtcNow;
        c.CeoUserId = actorUserId;
        c.CeoApproved = approve;
        c.CeoComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        c.CeoDecisionAt = now;
        c.LastActivityAt = now;

        if (approve)
        {
            c.Status = COMMITTEE_REVIEW_V1_CaseStatus.Completed;
            c.CompletedAt = now;
            await store.SaveChangesAsync(ct);
            await NotifyApprovedAsync(c, ct);
        }
        else
        {
            // 執行長否決為終局 — 路由至 end_rejected，不可重送。
            c.Status = COMMITTEE_REVIEW_V1_CaseStatus.Rejected;
            c.CompletedAt = now;
            await store.SaveChangesAsync(ct);
            await NotifyRejectedAsync(c, c.CeoComment ?? "（未填寫否決原因）", ct);
        }
        return c;
    }

    // ── Withdraw / abandon (baseline 撤回 + task_revise 放棄送審) ───────────────
    public async Task<COMMITTEE_REVIEW_V1_Case> CancelAsync(Guid caseId, Guid actorUserId, CancellationToken ct)
    {
        var c = await store.FindByIdAsync(caseId, ct)
                ?? throw new NotFoundException(nameof(COMMITTEE_REVIEW_V1_Case), caseId);
        if (c.SubmitterUserId != actorUserId)
            throw new ForbiddenException("只有申請人本人可以撤回此案件");
        if (c.Status is COMMITTEE_REVIEW_V1_CaseStatus.Completed
            or COMMITTEE_REVIEW_V1_CaseStatus.Rejected
            or COMMITTEE_REVIEW_V1_CaseStatus.Cancelled)
            throw new ConflictException("此案件已結案，無法撤回");

        var now = clock.UtcNow;
        c.Status = COMMITTEE_REVIEW_V1_CaseStatus.Cancelled;
        c.CompletedAt = now;
        c.LastActivityAt = now;
        await store.SaveChangesAsync(ct);
        // Any still-Open parallel slots are ignored by the inbox (it filters on
        // case status), so no explicit group teardown is required.
        return c;
    }

    // ── helpers ─────────────────────────────────────────────────────────────
    private Task OpenParallelAsync(COMMITTEE_REVIEW_V1_Case c, CancellationToken ct) =>
        parallel.OpenAsync(FlowCode, FlowVersion, c.Id, ReviewGatewayKey(c.CurrentRound),
            new List<SlotSpec>
            {
                new(FinanceNodeId, FinanceRole, null),
                new(ProcurementNodeId, ProcurementRole, null),
                new(ItNodeId, ItRole, null),
            },
            threshold: Quorum, ct);

    private static string RejectedReason(ParallelApprovalGroup group, string? comment)
    {
        var rejected = group.Slots.FirstOrDefault(s => s.Decision == SlotDecision.Rejected);
        return rejected?.Comment ?? comment ?? "（未填寫退回意見）";
    }

    private string CaseUrl(COMMITTEE_REVIEW_V1_Case c) => $"/cases/committee_review/{c.Id}";
    private static string FmtAmount(decimal amount) => $"NT$ {amount:N0}";
    private string ExecPeriod(COMMITTEE_REVIEW_V1_Case c) =>
        $"{c.ExecStart:yyyy-MM-dd} ~ {c.ExecEnd:yyyy-MM-dd}";

    public static string CategoryLabel(string category) => category switch
    {
        "major_procurement" => "重大採購",
        "capital_expenditure" => "資本支出",
        "capex" => "資本支出",
        "other" => "其他",
        _ => category,
    };

    private IReadOnlyDictionary<string, string?> Ctx(COMMITTEE_REVIEW_V1_Case c) =>
        new Dictionary<string, string?>
        {
            ["caseId"] = c.Id.ToString(),
            ["flowCode"] = FlowCode,
            ["flowVersion"] = FlowVersion.ToString(),
        };

    private async Task NotifyCommitteeAsync(COMMITTEE_REVIEW_V1_Case c, bool resubmit, CancellationToken ct)
    {
        var memberIds = new List<Guid>();
        memberIds.AddRange(await directory.GetUsersInRoleAsync(FinanceRole, ct));
        memberIds.AddRange(await directory.GetUsersInRoleAsync(ProcurementRole, ct));
        memberIds.AddRange(await directory.GetUsersInRoleAsync(ItRole, ct));
        memberIds = memberIds.Distinct().ToList();
        if (memberIds.Count == 0) return;

        var lookups = await directory.GetManyAsync(memberIds, ct);
        var category = CategoryLabel(c.ReviewCategory);
        var r = resubmit
            ? COMMITTEE_REVIEW_V1_NotificationTemplates.RenderResubmitCommittee(
                c.CaseTitle, category, FmtAmount(c.ApplyAmount), ExecPeriod(c), c.RevisionNote ?? "—", CaseUrl(c))
            : COMMITTEE_REVIEW_V1_NotificationTemplates.RenderSubmitCommittee(
                c.CaseTitle, category, FmtAmount(c.ApplyAmount), ExecPeriod(c), CaseUrl(c));

        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_{(resubmit ? "resubmit" : "submit")}_committee",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: memberIds.Select(id => new NotifyRecipient(
                id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyCeoAsync(COMMITTEE_REVIEW_V1_Case c, int approvedCount, CancellationToken ct)
    {
        var ceoIds = (await directory.GetUsersInRoleAsync(CeoRole, ct)).Distinct().ToList();
        if (ceoIds.Count == 0) return;
        var lookups = await directory.GetManyAsync(ceoIds, ct);
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderCeoAssign(
            c.CaseTitle, CategoryLabel(c.ReviewCategory), FmtAmount(c.ApplyAmount), ExecPeriod(c), approvedCount, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_ceo_on_assign",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: ceoIds.Select(id => new NotifyRecipient(
                id, lookups.GetValueOrDefault(id)?.Email, lookups.GetValueOrDefault(id)?.DisplayName)).ToList(),
            Context: Ctx(c)), ct);
    }

    private async Task NotifyReturnAsync(COMMITTEE_REVIEW_V1_Case c, string reason, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderReturnApplicant(
            c.CaseTitle, FmtAmount(c.ApplyAmount), reason, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_return_to_applicant",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private async Task NotifyApprovedAsync(COMMITTEE_REVIEW_V1_Case c, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderApprovedApplicant(
            c.CaseTitle, CategoryLabel(c.ReviewCategory), FmtAmount(c.ApplyAmount), ExecPeriod(c),
            c.CeoComment ?? "—", CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_approved_to_applicant",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private async Task NotifyRejectedAsync(COMMITTEE_REVIEW_V1_Case c, string reason, CancellationToken ct)
    {
        var submitter = await directory.GetByIdAsync(c.SubmitterUserId, ct);
        var r = COMMITTEE_REVIEW_V1_NotificationTemplates.RenderRejectedApplicant(
            c.CaseTitle, CategoryLabel(c.ReviewCategory), FmtAmount(c.ApplyAmount), reason, CaseUrl(c));
        await notify.DispatchAsync(new NotifyMessage(
            SourceId: $"{FlowCode}_{FlowVersion}.notify_rejected_to_applicant",
            Subject: r.Subject, Body: r.Body, Channels: new[] { "email", "in_app" },
            Recipients: new[] { new NotifyRecipient(c.SubmitterUserId, submitter?.Email, submitter?.DisplayName) },
            Context: Ctx(c)), ct);
    }

    private static void Validate(SubmitInput input)
    {
        if (string.IsNullOrWhiteSpace(input.CaseTitle))
            throw Invalid(nameof(input.CaseTitle), "案由標題為必填");
        if (string.IsNullOrWhiteSpace(input.ReviewCategory))
            throw Invalid(nameof(input.ReviewCategory), "審議類別為必填");
        if (string.IsNullOrWhiteSpace(input.BenefitDescription))
            throw Invalid(nameof(input.BenefitDescription), "效益說明為必填");
        if (input.ApplyAmount < 0)
            throw Invalid(nameof(input.ApplyAmount), "申請金額不得為負數");
        if (input.ExecEnd < input.ExecStart)
            throw Invalid(nameof(input.ExecEnd), "執行迄日不能早於起日");
        if (input.AttachmentFileId is null)
            throw Invalid(nameof(input.AttachmentFileId), "附件為必填");
    }

    private static ValidationException Invalid(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}

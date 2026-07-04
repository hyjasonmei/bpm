namespace Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Persistent case for COMMITTEE_REVIEW V1 (委員會審議). Business data + minimal
/// workflow state. The concurrent 財務 / 採購 / 資訊 三委員並簽 decisions live in the
/// shared parallel-approval primitive (ParallelApprovalGroup/Slot), keyed by
/// (FlowCode, CaseId, gateway key) — NOT per-committee columns here. The final
/// single CEO 最終裁決 decision is a normal role-queue step, recorded on the
/// <c>Ceo*</c> columns.
/// </summary>
public class COMMITTEE_REVIEW_V1_Case
{
    public Guid Id { get; set; }

    // ── Business data (from the submit / revise form) ───────────────────────
    public Guid SubmitterUserId { get; set; }
    public string CaseTitle { get; set; } = string.Empty;
    public string ReviewCategory { get; set; } = string.Empty;
    public decimal ApplyAmount { get; set; }
    public string BenefitDescription { get; set; } = string.Empty;
    public DateOnly ExecStart { get; set; }
    public DateOnly ExecEnd { get; set; }
    public Guid? AttachmentFileId { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Applicant's note explaining what the latest revision changed.</summary>
    public string? RevisionNote { get; set; }

    // ── Workflow state ──────────────────────────────────────────────────────
    public COMMITTEE_REVIEW_V1_CaseStatus Status { get; set; } = COMMITTEE_REVIEW_V1_CaseStatus.PendingParallelReview;

    /// <summary>
    /// Which parallel-review round is live (1-based). Each 退回→重新送審 bumps this so
    /// the re-opened parallel group gets a distinct gateway key and the case's
    /// <c>GetAsync(caseId, gatewayKey)</c> stays deterministic across rounds.
    /// </summary>
    public int CurrentRound { get; set; } = 1;

    // Final CEO (執行長) 最終裁決 decision.
    public Guid? CeoUserId { get; set; }
    public bool? CeoApproved { get; set; }
    public string? CeoComment { get; set; }
    public DateTime? CeoDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

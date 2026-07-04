namespace Bpm.Domain.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Persistent case for CONTRACT_REVIEW V1 (合約審查). Business data + minimal
/// workflow state. The concurrent LEGAL/FINANCE 並簽 decisions live in the shared
/// parallel-approval primitive (ParallelApprovalGroup/Slot), keyed by
/// (FlowCode, CaseId, gateway key) — NOT per-approver columns here. The final
/// single LEGAL_MANAGER 定案歸檔 decision is a normal role-queue step, recorded
/// on the <c>LegalManager*</c> columns.
/// </summary>
public class CONTRACT_REVIEW_V1_Case
{
    public Guid Id { get; set; }

    // ── Business data (from the submit / revise form) ───────────────────────
    public Guid SubmitterUserId { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public string ContractSubject { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public Guid? DraftFileId { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Applicant's note explaining what the latest revision changed.</summary>
    public string? RevisionNote { get; set; }

    // ── Workflow state ──────────────────────────────────────────────────────
    public CONTRACT_REVIEW_V1_CaseStatus Status { get; set; } = CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview;

    /// <summary>
    /// Which parallel-review round is live (1-based). Each 退回→重新送審 bumps this so
    /// the re-opened parallel group gets a distinct gateway key and the case's
    /// <c>GetAsync(caseId, gatewayKey)</c> stays deterministic across rounds.
    /// </summary>
    public int CurrentRound { get; set; } = 1;

    // Final LEGAL_MANAGER (法務主管) 定案歸檔 decision.
    public Guid? LegalManagerUserId { get; set; }
    public bool? LegalManagerApproved { get; set; }
    public string? LegalManagerComment { get; set; }
    public DateTime? LegalManagerDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

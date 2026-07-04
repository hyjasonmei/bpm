namespace Bpm.Domain.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// CONTRACT_REVIEW V1 lifecycle (合約審查).
///
/// Submit opens a parallel review gateway (LEGAL + FINANCE 並簽, threshold 2/2 =
/// AND); the case sits in <see cref="PendingParallelReview"/> until the group
/// resolves:
///  • both approve → <see cref="PendingLegalManager"/> (法務主管定案歸檔, a single
///    LEGAL_MANAGER approval) → <see cref="Completed"/>;
///  • any reject → <see cref="ResubmitRequired"/> — the applicant revises and
///    either re-sends審 (re-opens a fresh parallel round) or abandons
///    (<see cref="Cancelled"/>).
///
/// A submitter may withdraw (撤回) any non-terminal case → <see cref="Cancelled"/>.
/// </summary>
public enum CONTRACT_REVIEW_V1_CaseStatus
{
    PendingParallelReview = 0,
    ResubmitRequired = 1,
    PendingLegalManager = 2,
    Completed = 3,
    Cancelled = 4,
}

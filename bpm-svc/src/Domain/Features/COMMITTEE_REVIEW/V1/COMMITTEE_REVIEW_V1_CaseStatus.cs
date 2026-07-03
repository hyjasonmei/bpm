namespace Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// COMMITTEE_REVIEW V1 lifecycle. Submit opens a 3-member committee parallel
/// gateway (財務 + 法務 + 採購) with a QUORUM threshold of 2/3 (門檻 M-of-N) —
/// any 2 approvals pass; the 3rd slot is auto-skipped. Any single reject fails
/// the whole case (v1 rule). Demonstrates the threshold variant of 並簽.
/// </summary>
public enum COMMITTEE_REVIEW_V1_CaseStatus
{
    PendingCommittee = 0,
    Completed = 1,
    Rejected = 2,
}

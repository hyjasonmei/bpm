namespace Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// COMMITTEE_REVIEW V1 lifecycle (委員會審議).
///
/// Submit opens a parallel review gateway (財務 / 採購 / 資訊 三委員並簽, 門檻 2/3 =
/// quorum) via the shared parallel-approval primitive; the case sits in
/// <see cref="PendingParallelReview"/> until the group resolves:
///  • ≥2 委員 approve → <see cref="PendingCeo"/> (執行長最終裁決) →
///    <see cref="Completed"/> (approve) or <see cref="Rejected"/> (reject, 終局);
///  • 任一委員 reject → <see cref="ResubmitRequired"/> — the applicant revises and
///    either re-sends審 (re-opens a fresh 3-slot parallel round) or abandons
///    (<see cref="Cancelled"/>).
///
/// A submitter may withdraw (撤回) any non-terminal case → <see cref="Cancelled"/>.
/// </summary>
public enum COMMITTEE_REVIEW_V1_CaseStatus
{
    PendingParallelReview = 0,
    ResubmitRequired = 1,
    PendingCeo = 2,
    Completed = 3,
    Rejected = 4,
    Cancelled = 5,
}

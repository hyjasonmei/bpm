namespace Bpm.Domain.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// CONTRACT_REVIEW V1 lifecycle. Submit opens a parallel review gateway
/// (LEGAL + FINANCE 並簽, threshold N/N); the case sits in
/// <see cref="PendingParallelReview"/> until the parallel group resolves —
/// all approve → Completed, any reject → Rejected.
/// </summary>
public enum CONTRACT_REVIEW_V1_CaseStatus
{
    PendingParallelReview = 0,
    Completed = 1,
    Rejected = 2,
}

namespace Bpm.Domain.Features.COMMITTEE_REVIEW.V1;

/// <summary>
/// Persistent case for COMMITTEE_REVIEW V1 (委員會審議). Business data + minimal
/// workflow state; the concurrent per-member decisions live in the shared
/// parallel-approval primitive (threshold 2/3 quorum).
/// </summary>
public class COMMITTEE_REVIEW_V1_Case
{
    public Guid Id { get; set; }

    public Guid SubmitterUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NTD";
    public string Purpose { get; set; } = string.Empty;

    public COMMITTEE_REVIEW_V1_CaseStatus Status { get; set; } = COMMITTEE_REVIEW_V1_CaseStatus.PendingCommittee;

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

namespace Bpm.Domain.Features.CONTRACT_REVIEW.V1;

/// <summary>
/// Persistent case for CONTRACT_REVIEW V1 (合約審查). Business data + minimal
/// workflow state. The concurrent per-approver decisions live in the shared
/// parallel-approval primitive (ParallelApprovalGroup/Slot), keyed by
/// (FlowCode, CaseId, GatewayNodeId) — NOT per-approver columns here.
/// </summary>
public class CONTRACT_REVIEW_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NTD";
    public Guid? ContractFileId { get; set; }

    // Workflow state
    public CONTRACT_REVIEW_V1_CaseStatus Status { get; set; } = CONTRACT_REVIEW_V1_CaseStatus.PendingParallelReview;

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

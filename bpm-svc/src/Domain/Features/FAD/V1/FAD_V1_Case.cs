namespace Bpm.Domain.Features.FAD.V1;

/// <summary>
/// Persistent case for the FAD (Fixed Asset Disposal) V1 flow. Disposal
/// request (reason + asset + optional photo) → IT judgment
/// (submitter.manager) → confirmation (零件轉備品 / 失效) → disposed.
/// Reject loops back to <c>ResubmitRequired</c>. POCO only.
/// </summary>
public class FAD_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public string DisposalReason { get; set; } = string.Empty;   // 損壞報廢 / 老舊報廢 / 遺失報廢
    public string AssetId { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? PhotoFileId { get; set; }                       // FilePicker upload id

    // Workflow state
    public FAD_V1_CaseStatus Status { get; set; } = FAD_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // IT judgment (submitter.manager) decision.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    // Confirmation (goods receipt / handling).
    public Guid? ConfirmedByUserId { get; set; }
    public string? HandlingResult { get; set; }                  // 零件轉備品 / 失效
    public string? ConfirmRemark { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

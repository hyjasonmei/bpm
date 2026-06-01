namespace Bpm.Domain.Features.FAP.V1;

/// <summary>
/// Persistent case for the FAP (Fixed Asset Purchase) V1 flow.
/// Request (purchase-item repeater + shipping / charge / purpose) →
/// manager approval → PO issued (service step, no-op in POC) →
/// verification (goods receipt) → booked into the asset list.
///
/// Manager reject loops back to <c>ResubmitRequired</c>. Verification
/// is performed by the original submitter (goods receipt confirmation).
/// POCO only.
/// </summary>
public class FAP_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public List<FAP_V1_PurchaseItem> PurchaseItems { get; set; } = new();
    public string ShippingLocation { get; set; } = string.Empty;
    public string ChargeTo { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;      // New | Replacement | Project
    public DateOnly? ExpectedDate { get; set; }
    public string? Note { get; set; }

    // Workflow state
    public FAP_V1_CaseStatus Status { get; set; } = FAP_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Manager (submitter.manager) decision.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    // PO issued marker (service step).
    public string? PurchaseOrderNo { get; set; }
    public DateTime? PoIssuedAt { get; set; }

    // Verification (goods receipt).
    public Guid? VerifiedByUserId { get; set; }
    public string? Received { get; set; }                    // Received | Rejected
    public string? VerificationRemark { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

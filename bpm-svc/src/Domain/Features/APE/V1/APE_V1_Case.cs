namespace Bpm.Domain.Features.APE.V1;

/// <summary>
/// Persistent case for the APE (Advance Payment Employee / Cash in
/// Advance) V1 flow. Holds the submitter's cash-advance request plus
/// per-stage workflow state (Status enum, current assignee, manager
/// decision columns). One row per submitted request.
///
/// Single approval node (<c>ap</c> = submitter.manager). Its reject
/// edge loops back to the submit task (<c>ape</c>), so a rejected case
/// lands in <c>ResubmitRequired</c> with the assignee reverted to the
/// original submitter; on resubmit it re-enters <c>PendingManager</c>.
/// No cancel / revoke action in the spec. POCO only.
/// </summary>
public class APE_V1_Case
{
    public Guid Id { get; set; }

    // Business data — mirrors the bundle's userTask `ape` fields.
    public Guid SubmitterUserId { get; set; }
    public DateOnly ExpectReceiveDate { get; set; }
    public DateOnly DeductReturnDate { get; set; }
    public string ChargeDepartment { get; set; } = string.Empty;
    public string? RechargeOutside { get; set; }            // Yes | No
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;    // NTD | USD

    // Workflow state
    public APE_V1_CaseStatus Status { get; set; } = APE_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Manager (submitter.manager) decision columns.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

namespace Bpm.Domain.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Persistent case for the PURCHASE_REQUEST V1 flow. Holds the
/// submitter's payload (Invoices repeater + the submit comment) plus
/// per-stage workflow state (Status enum, current assignee, per-role
/// decision columns). One row per submitted purchase request.
///
/// Rejection at either gateway loops back to <c>ResubmitRequired</c>
/// with the assignee reverted to the original submitter; on resubmit
/// the case re-enters <c>PendingDeptHead</c>. The submitter may
/// withdraw their own case from any non-terminal state, ending it at
/// <c>Cancelled</c> (baseline withdraw, see CancelAsync).
/// </summary>
public class PURCHASE_REQUEST_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public string SubmitterComment { get; set; } = string.Empty;
    public List<PURCHASE_REQUEST_V1_Invoice> Invoices { get; set; } = new();

    // Workflow state
    public PURCHASE_REQUEST_V1_CaseStatus Status { get; set; } = PURCHASE_REQUEST_V1_CaseStatus.PendingDeptHead;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Per-stage approver columns. Approved=true/false captures the
    // decision; null = not yet acted; Comment + DecisionAt populated
    // on action. On any reject the corresponding columns are cleared
    // before the next round so the timeline reflects the latest pass.
    public Guid? DeptHeadUserId { get; set; }
    public bool? DeptHeadApproved { get; set; }
    public string? DeptHeadComment { get; set; }
    public DateTime? DeptHeadDecisionAt { get; set; }

    public Guid? FinanceUserId { get; set; }
    public bool? FinanceApproved { get; set; }
    public string? FinanceComment { get; set; }
    public DateTime? FinanceDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

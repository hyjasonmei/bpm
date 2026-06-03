namespace Bpm.Domain.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Persistent case for the VENDOR_EXPENSE V1 flow (採購申請流程). Holds
/// the submitter's payload (Vendor + Invoices repeater + an optional
/// submit comment) plus per-stage workflow state (Status enum, current
/// assignee, per-stage decision columns). One row per submitted case.
///
/// Approval chain: 主管審核 (Supervisor) → 採購審定 (Procurement) →
/// 簽核 (Sign). Rejection at any stage loops back to
/// <see cref="VENDOR_EXPENSE_V1_CaseStatus.ResubmitRequired"/> with the
/// assignee reverted to the original submitter; on resubmit the case
/// re-enters <see cref="VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor"/>.
/// The submitter may withdraw their own case from any non-terminal
/// state, ending it at <c>Cancelled</c> (baseline withdraw).
///
/// Stages 1 and 3 both resolve to <c>submitter.department.head</c> per
/// the spec, so the same person reviews then formally signs off — the
/// columns are kept separate so the timeline records both passes.
/// </summary>
public class VENDOR_EXPENSE_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public string? Vendor { get; set; }
    public string? SubmitterComment { get; set; }
    public List<VENDOR_EXPENSE_V1_Invoice> Invoices { get; set; } = new();

    // Workflow state
    public VENDOR_EXPENSE_V1_CaseStatus Status { get; set; } = VENDOR_EXPENSE_V1_CaseStatus.PendingSupervisor;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Per-stage approver columns. Approved=true/false captures the
    // decision; null = not yet acted; Comment + DecisionAt populated on
    // action. On any reject every stage's columns are cleared before the
    // next round so the timeline reflects the latest pass only.
    public Guid? SupervisorUserId { get; set; }
    public bool? SupervisorApproved { get; set; }
    public string? SupervisorComment { get; set; }
    public DateTime? SupervisorDecisionAt { get; set; }

    public Guid? ProcurementUserId { get; set; }
    public bool? ProcurementApproved { get; set; }
    public string? ProcurementComment { get; set; }
    public DateTime? ProcurementDecisionAt { get; set; }

    public Guid? SignUserId { get; set; }
    public bool? SignApproved { get; set; }
    public string? SignComment { get; set; }
    public DateTime? SignDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

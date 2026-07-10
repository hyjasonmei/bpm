namespace Bpm.Domain.Features.OVERTIME.V1;

/// <summary>
/// Persistent case for the OVERTIME V1 flow (加班申請). Holds the submitter's
/// payload (加班日期 / 起訖時間 / 預估時數 / 事由) plus per-stage workflow state
/// (Status enum, current assignee, per-role decision columns). One row per
/// submitted overtime request. POCO only — no EF / service references.
///
/// <para><see cref="MonthlyHours"/> caches the value the manager-approval gateway
/// evaluated (this case's 預估時數 + the submitter's other Completed overtime hours
/// in the same calendar month). It is written once, when the manager approves, so
/// the case detail can explain why the case did or did not route to HR review.</para>
///
/// Rejection at either gate is terminal (<see cref="OVERTIME_V1_CaseStatus.RejectedByManager"/>
/// / <see cref="OVERTIME_V1_CaseStatus.RejectedByHr"/>); the submitter may withdraw
/// their own case from any non-terminal state (<see cref="OVERTIME_V1_CaseStatus.Cancelled"/>).
/// </summary>
public class OVERTIME_V1_Case
{
    public Guid Id { get; set; }

    // ── Business data (task_apply fields) ──────────────────────────────────
    public Guid SubmitterUserId { get; set; }
    public DateOnly OvertimeDate { get; set; }
    public string? StartTime { get; set; }          // free-text HH:mm (spec: text, optional)
    public string? EndTime { get; set; }            // free-text HH:mm (spec: text, optional)
    public decimal? EstimatedHours { get; set; }     // spec: number, optional
    public string OvertimeReason { get; set; } = string.Empty;

    // ── Workflow state ─────────────────────────────────────────────────────
    public OVERTIME_V1_CaseStatus Status { get; set; } = OVERTIME_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public string? CurrentAssigneeRoleCode { get; set; }   // non-null while pending on a shared role queue (HR); UserId is null then

    /// <summary>Cumulative monthly overtime hours the manager-approval gateway saw.</summary>
    public decimal? MonthlyHours { get; set; }

    // ── Per-stage approver columns ─────────────────────────────────────────
    // Approved = true/false captures the decision; null = not yet acted.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public Guid? HrUserId { get; set; }
    public bool? HrApproved { get; set; }
    public string? HrComment { get; set; }
    public DateTime? HrDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

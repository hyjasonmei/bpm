namespace Bpm.Domain.Features.LEAVE.V1;

/// <summary>
/// Persistent case for the LEAVE V1 flow. Holds business data
/// (leave_type / date_range / days / reason / cert) plus per-stage
/// workflow state (Status enum, current assignee, per-role decision
/// columns). One row per submitted leave request.
/// </summary>
public class LEAVE_V1_Case
{
    public Guid Id { get; set; }

    // Business data — mirrors spec.userTasks[task_apply].fields[].id
    public Guid SubmitterUserId { get; set; }
    public string LeaveType { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal Days { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid? CertFileId { get; set; }

    // HR archive task body
    public string? ArchiveNote { get; set; }

    // Workflow state
    public LEAVE_V1_CaseStatus Status { get; set; } = LEAVE_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public string? CurrentAssigneeRoleCode { get; set; }   // shared-role-queue: non-null = pending on this role (any holder can act); UserId is null then

    // Per-stage approver columns — Approved=true/false captures the decision;
    // null = not yet acted; Comment + DecisionAt populated on action.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public Guid? VpUserId { get; set; }
    public bool? VpApproved { get; set; }
    public string? VpComment { get; set; }
    public DateTime? VpDecisionAt { get; set; }

    public Guid? HrUserId { get; set; }
    public DateTime? HrArchivedAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

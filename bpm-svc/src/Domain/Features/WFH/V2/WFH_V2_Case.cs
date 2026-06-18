namespace Bpm.Domain.Features.WFH.V2;

/// <summary>
/// Persistent case for the WFH (居家辦公申請) V2 flow. Holds the
/// employee's work-from-home request (apply date / WFH date range /
/// computed consecutive days / reason / optional attachment) plus
/// per-stage workflow state (Status enum, current assignee, manager +
/// senior decision columns). One row per submitted request.
///
/// Two approval stages: <c>approval_manager</c> (always) and
/// <c>approval_senior</c> (only when <see cref="Days"/> &gt;= 15, routed
/// by <c>gateway_days</c>). The senior approver resolves to
/// submitter.manager.manager (falling back to submitter.manager when the
/// manager is already top of the chain). POCO only — no EF / service refs.
/// </summary>
public class WFH_V2_Case
{
    public Guid Id { get; set; }

    // Business data — mirrors spec.userTasks[task_apply].fields[].id.
    // `applicant` is fixed to the logged-in submitter (spec
    // permissions.submitter = self), so SubmitterUserId carries it.
    public Guid SubmitterUserId { get; set; }
    public DateOnly ApplyDate { get; set; }
    public DateOnly StartDate { get; set; }      // wfh_date_range.start
    public DateOnly EndDate { get; set; }        // wfh_date_range.end
    public int Days { get; set; }                // consecutive calendar days (inclusive)
    public string Reason { get; set; } = string.Empty;
    public Guid? AttachmentFileId { get; set; }  // optional file upload

    // Workflow state
    public WFH_V2_CaseStatus Status { get; set; } = WFH_V2_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Manager (submitter.manager) decision columns.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    // Senior (submitter.manager.manager) decision columns — only used on
    // the days >= 15 branch.
    public Guid? SeniorUserId { get; set; }
    public bool? SeniorApproved { get; set; }
    public string? SeniorComment { get; set; }
    public DateTime? SeniorDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

namespace Bpm.Domain.Features.ETM.V1;

/// <summary>
/// Persistent case for the ETM (Employee Termination) V1 flow.
/// Termination request → manager approval → handover (asset / account
/// return + outstanding payment) → done. Reject loops back to
/// <c>ResubmitRequired</c>. Handover completed by the original
/// submitter. POCO only.
/// </summary>
public class ETM_V1_Case
{
    public Guid Id { get; set; }

    // Termination request data
    public Guid SubmitterUserId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public DateOnly LastWorkingDate { get; set; }
    public string Reason { get; set; } = string.Empty;           // Resignation | Retirement | Termination
    public string? ProvideCertificate { get; set; }             // Yes | No

    // Handover (filled at the handover stage).
    public string? OutstandingPayment { get; set; }             // Yes | No
    public List<ETM_V1_ReturnItem> ReturnItems { get; set; } = new();

    // Workflow state
    public ETM_V1_CaseStatus Status { get; set; } = ETM_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public Guid? HandoverByUserId { get; set; }
    public DateTime? HandoverAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

namespace Bpm.Domain.Features.TEO.V1;

/// <summary>
/// Persistent case for the TEO (Travel Expense) V1 flow. Submitter
/// payload (linked travel-request no + expense line-item repeater) plus
/// two-stage workflow state: manager (submitter.manager) then finance
/// (role:Finance). Reject at either stage loops back to
/// <c>ResubmitRequired</c> (assignee reverted to submitter); resubmit
/// re-enters <c>PendingManager</c>. POCO only.
/// </summary>
public class TEO_V1_Case
{
    public Guid Id { get; set; }

    // Business data
    public Guid SubmitterUserId { get; set; }
    public string TravelRequestNo { get; set; } = string.Empty;
    public List<TEO_V1_ExpenseItem> ExpenseItems { get; set; } = new();

    // Workflow state
    public TEO_V1_CaseStatus Status { get; set; } = TEO_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public Guid? FinanceUserId { get; set; }
    public bool? FinanceApproved { get; set; }
    public string? FinanceComment { get; set; }
    public DateTime? FinanceDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

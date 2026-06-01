namespace Bpm.Domain.Features.EOB.V1;

/// <summary>
/// Persistent case for the EOB (Employee Onboarding) V1 flow. New-hire
/// request → manager approval → basic setup (onboarding-task checklist)
/// → onboarded. Reject loops back to <c>ResubmitRequired</c>. Setup is
/// completed by the original submitter (hiring manager / coordinator).
/// POCO only.
/// </summary>
public class EOB_V1_Case
{
    public Guid Id { get; set; }

    // New-hire request data
    public Guid SubmitterUserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string BusinessTitle { get; set; } = string.Empty;
    public string EmployeeLocation { get; set; } = string.Empty;   // APAC | EMEA | AMERICAS
    public DateOnly OnboardDate { get; set; }
    public string? RequireMailbox { get; set; }                    // Yes | No
    public string CostCenter { get; set; } = string.Empty;
    public string? ContractNumber { get; set; }
    public DateOnly? ContractEffectiveDate { get; set; }
    public DateOnly? ContractExpirationDate { get; set; }

    // Setup checklist (filled at the setup stage).
    public List<EOB_V1_SetupTask> SetupTasks { get; set; } = new();

    // Workflow state
    public EOB_V1_CaseStatus Status { get; set; } = EOB_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public Guid? SetupByUserId { get; set; }
    public DateTime? SetupAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

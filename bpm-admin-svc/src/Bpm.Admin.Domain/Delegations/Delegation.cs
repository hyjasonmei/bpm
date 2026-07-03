namespace Bpm.Admin.Domain.Delegations;

/// Acceptance state of a delegation. A newly-created delegation starts Pending
/// and does NOT take effect until the delegate Accepts it. Declined rows are
/// kept (Active=false) for history.
public enum DelegationStatus
{
    Pending  = 0,
    Accepted = 1,
    Declined = 2,
}

public class Delegation
{
    public Guid Id { get; set; }
    public Guid DelegatorPrincipalId { get; set; }
    public Guid DelegateToUserId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool Active { get; set; } = true;                              // lifecycle: current (not cancelled/superseded)
    public DelegationStatus Status { get; set; } = DelegationStatus.Pending;  // acceptance state
    public DateTime? RespondedAt { get; set; }                            // when the delegate accepted / declined
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

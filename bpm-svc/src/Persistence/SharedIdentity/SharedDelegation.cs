namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_Delegations. Unlike the other SharedX mirrors this one is
// written by bpm-svc too — delegation is end-user self-service set from bpm-ui,
// and bpm-ui does not call admin-svc directly. Single-source so admin-set and
// self-set delegations share one table that the runtime honors. (POC deviation
// from the strict read-only SharedX contract — see the delegation spec.)
public class SharedDelegation
{
    public Guid Id { get; set; }
    public Guid DelegatorPrincipalId { get; set; }
    public Guid DelegateToUserId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool Active { get; set; } = true;
    public int Status { get; set; }                 // 0=Pending 1=Accepted 2=Declined (Bpm.Application.Delegation.DelegationStatus)
    public DateTime? RespondedAt { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

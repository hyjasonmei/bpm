namespace Bpm.Admin.Domain.Delegations;

public class Delegation
{
    public Guid Id { get; set; }
    public Guid DelegatorPrincipalId { get; set; }
    public Guid DelegateToUserId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool Active { get; set; } = true;
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

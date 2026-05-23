namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_DeptHeads. ActorRef.dept_head resolution chains
// SharedUserDept (IsPrimary) → SharedDeptHead → HeadUserId.
public class SharedDeptHead
{
    public Guid DeptId { get; set; }
    public Guid HeadUserId { get; set; }
    public DateTime AssignedAt { get; set; }
}

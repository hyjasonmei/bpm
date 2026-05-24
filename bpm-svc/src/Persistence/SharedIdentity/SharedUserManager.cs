namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_UserManagers. Direct user→manager edge owned by
// admin-svc. ActorRef.manager resolution reads this.
public class SharedUserManager
{
    public Guid UserId { get; set; }
    public Guid ManagerUserId { get; set; }
    public DateTime AssignedAt { get; set; }
}

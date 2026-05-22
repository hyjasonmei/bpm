namespace Bpm.Admin.Domain.Roles;

public class PrincipalRole
{
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public bool InheritToMembers { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
}

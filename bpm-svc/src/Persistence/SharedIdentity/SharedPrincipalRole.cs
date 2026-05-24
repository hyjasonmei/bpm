namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_PrincipalRoles. `InheritToMembers = true` means a role
// assigned to a Dept or Group implicitly applies to every member principal.
// Callers that need the *effective* set of roles for a user must walk
// UserDepts + GroupMembers + DeptParents and union role names — see
// SharedRoleResolver.
public class SharedPrincipalRole
{
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public bool InheritToMembers { get; set; }
    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
}

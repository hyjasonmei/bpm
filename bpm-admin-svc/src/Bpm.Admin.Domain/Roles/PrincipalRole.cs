namespace Bpm.Admin.Domain.Roles;

public class PrincipalRole
{
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public bool InheritToMembers { get; set; }

    /// <summary>
    /// Only meaningful when the principal is a Dept and InheritToMembers is
    /// true: the role additionally reaches members of every DESCENDANT dept,
    /// not just this dept's direct members. Explicit opt-in so hierarchy-wide
    /// grants are a visible choice — the admin effective-role resolver and
    /// bpm-svc's routing must read this same flag.
    /// </summary>
    public bool IncludeSubDepts { get; set; }

    public DateTime AssignedAt { get; set; }
    public Guid? AssignedByUserId { get; set; }
}

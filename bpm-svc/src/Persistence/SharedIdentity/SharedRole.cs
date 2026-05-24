namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_Roles. The `Name` column is the role identifier used
// throughout the bpm runtime (FORMS.ownerByStep, JWT `roles` claim,
// canAct() checks). admin-svc's RBAC console displays Name as both the
// human label and the machine key.
public class SharedRole
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}

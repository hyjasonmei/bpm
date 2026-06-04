namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_Roles. `Code` is the stable role identifier used
// throughout the bpm runtime (JWT `roles` claim, actor resolution
// role:<code>, canAct() checks); `Name` is the human display label.
public class SharedRole
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}

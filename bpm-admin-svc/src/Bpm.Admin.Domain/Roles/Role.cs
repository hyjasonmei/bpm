namespace Bpm.Admin.Domain.Roles;

public class Role
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}

namespace Bpm.Domain.Entities.Org;

public sealed class User : Principal
{
    public User() { Type = PrincipalType.User; }

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid? ManagerId { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;

    public User? Manager { get; set; }
    public Department? Department { get; set; }
}

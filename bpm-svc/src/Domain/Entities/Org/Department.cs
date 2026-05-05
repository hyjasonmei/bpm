namespace Bpm.Domain.Entities.Org;

public sealed class Department : Principal
{
    public Department() { Type = PrincipalType.Department; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public Guid? HeadUserId { get; set; }

    public Department? Parent { get; set; }
    public User? Head { get; set; }
}

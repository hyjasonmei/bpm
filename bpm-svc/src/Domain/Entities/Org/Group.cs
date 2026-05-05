namespace Bpm.Domain.Entities.Org;

public sealed class Group : Principal
{
    public Group() { Type = PrincipalType.Group; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}

namespace Bpm.Admin.Domain.Principals;

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid MemberPrincipalId { get; set; }
    public PrincipalType MemberType { get; set; }
}

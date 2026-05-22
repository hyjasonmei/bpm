namespace Bpm.Admin.Application.Principals;

public class GroupCycleException : Exception
{
    public GroupCycleException(Guid groupId, Guid memberPrincipalId)
        : base($"Adding principal {memberPrincipalId} to group {groupId} would create a cycle in the group graph.")
    {
        GroupId = groupId;
        MemberPrincipalId = memberPrincipalId;
    }

    public Guid GroupId { get; }
    public Guid MemberPrincipalId { get; }
}

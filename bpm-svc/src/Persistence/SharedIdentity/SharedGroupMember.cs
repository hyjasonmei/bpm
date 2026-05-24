namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_GroupMembers. A group can contain users, sub-groups,
// or whole departments — `MemberType` mirrors admin's PrincipalType enum
// and decides which join the resolver should use.
public class SharedGroupMember
{
    public Guid GroupId { get; set; }
    public Guid MemberPrincipalId { get; set; }
    public SharedPrincipalType MemberType { get; set; }
}

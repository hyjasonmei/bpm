using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Org;

public sealed class GroupMember : AuditableEntity
{
    public Guid GroupId { get; set; }
    public Guid PrincipalId { get; set; }

    public Group? Group { get; set; }
    public Principal? Principal { get; set; }
}

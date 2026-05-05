using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Authz;

public sealed class RolePermission : AuditableEntity
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    public Role? Role { get; set; }
    public Permission? Permission { get; set; }
}

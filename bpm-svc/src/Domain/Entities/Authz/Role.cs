using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Authz;

public sealed class Role : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RoleScope Scope { get; set; } = RoleScope.System;
    public string? FlowCode { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

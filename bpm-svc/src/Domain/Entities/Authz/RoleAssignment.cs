using Bpm.Domain.Common;
using Bpm.Domain.Entities.Org;

namespace Bpm.Domain.Entities.Authz;

public sealed class RoleAssignment : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RoleId { get; set; }
    public Guid PrincipalId { get; set; }
    public AssignmentScope Scope { get; set; } = AssignmentScope.Tenant;
    public string? ScopeRef { get; set; }

    public Role? Role { get; set; }
    public Principal? Principal { get; set; }
}

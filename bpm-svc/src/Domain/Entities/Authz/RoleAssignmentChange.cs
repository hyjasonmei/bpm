using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Authz;

public sealed class RoleAssignmentChange : AuditableEntity, IImpersonable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ActorUserId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid RoleId { get; set; }
    public string RoleCodeSnapshot { get; set; } = string.Empty;
    public RoleAssignmentChangeAction Action { get; set; }
    public AssignmentScope Scope { get; set; }
    public string? ScopeRef { get; set; }
    public Guid? ImpersonatedByUserId { get; set; }
}

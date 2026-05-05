using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Org;

public abstract class Principal : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PrincipalType Type { get; protected set; }
    public string DisplayName { get; set; } = string.Empty;
}

using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Authz;

public sealed class Permission : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
}

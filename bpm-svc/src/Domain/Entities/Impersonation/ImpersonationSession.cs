using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Impersonation;

public sealed class ImpersonationSession : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImpersonatorUserId { get; set; }
    public Guid TargetUserId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public EndReason? EndReason { get; set; }
    public string Reason { get; set; } = string.Empty;
}

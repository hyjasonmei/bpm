using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Audit;

public sealed class ActorResolutionAudit : AuditableEntity, IImpersonable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string ActorRefJson { get; set; } = string.Empty;
    public Guid SubmitterUserId { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public string? StepCode { get; set; }
    public string ResultKind { get; set; } = string.Empty;   // Success | Failure
    public string? ResolvedUserIdsJson { get; set; }
    public string? ErrorKind { get; set; }
    public string? ErrorReason { get; set; }
    public Guid? ImpersonatedByUserId { get; set; }
}

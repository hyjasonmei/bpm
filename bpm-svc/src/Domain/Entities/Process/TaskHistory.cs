using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Process;

public sealed class TaskHistory : AuditableEntity, IImpersonable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";
    public Guid ProcessInstanceId { get; set; }
    public Guid? TaskId { get; set; }
    public HistoryEventType EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public string PayloadJson { get; set; } = "{}";

    public Guid? ImpersonatedByUserId { get; set; }
    public Guid? SandboxActualActor { get; set; }

    public ProcessInstance? Instance { get; set; }
    public ProcessTask? Task { get; set; }
}

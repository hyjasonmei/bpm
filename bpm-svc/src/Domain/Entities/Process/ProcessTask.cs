using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Process;

public sealed class ProcessTask : AuditableEntity, IImpersonable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";
    public Guid ProcessInstanceId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public NodeKind NodeKind { get; set; }
    public Guid? OriginalAssigneeUserId { get; set; }
    public Guid? ActualAssigneeUserId { get; set; }
    public string? CandidateSetJson { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Pending;
    public DateTime? DueAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Decision? Decision { get; set; }
    public string? Comment { get; set; }
    public string? FormDataPatchJson { get; set; }

    public Guid? ImpersonatedByUserId { get; set; }
    public Guid? SandboxActualActor { get; set; }

    public ProcessInstance? Instance { get; set; }
}

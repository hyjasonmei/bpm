using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Process;

public sealed class ProcessInstance : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";
    public string SpecCode { get; set; } = string.Empty;
    public int SpecVersion { get; set; }
    public string SpecSnapshotJson { get; set; } = "{}";
    public Guid InitiatorUserId { get; set; }
    public InstanceStatus Status { get; set; } = InstanceStatus.Running;
    public string CurrentFormDataJson { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public string? CancelReason { get; set; }
    public string? LastError { get; set; }
}

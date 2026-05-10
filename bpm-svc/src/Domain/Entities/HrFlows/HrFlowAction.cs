using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.HrFlows;

// Append-only audit row. Updates/deletes blocked at SaveChanges interceptor.
// Migrates to TaskHistory when add-process-runtime ships.
public sealed class HrFlowAction : AuditableEntity, IImpersonable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstanceId { get; set; }
    public Guid ActorUserId { get; set; }
    public HrFlowActionType Action { get; set; }
    public HrFlowStep FromStep { get; set; }
    public HrFlowStep ToStep { get; set; }
    public string? Comment { get; set; }
    public Guid? ImpersonatedByUserId { get; set; }

    public HrFlowInstance? Instance { get; set; }
}

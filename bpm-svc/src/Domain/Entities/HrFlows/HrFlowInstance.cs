using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.HrFlows;

// Interim implementation. When add-process-runtime ships, this entity migrates
// to ProcessInstance/ProcessTask/TaskHistory and this file is removed.
// See openspec/changes/add-hr-flows-resign-deptx/proposal.md (sunset section).
public sealed class HrFlowInstance : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public HrFlowSpecCode SpecCode { get; set; }
    public Guid InitiatorUserId { get; set; }
    public Guid ResolvedManagerUserId { get; set; }
    public HrFlowStatus Status { get; set; } = HrFlowStatus.PendingManager;
    public HrFlowStep CurrentStep { get; set; } = HrFlowStep.ManagerApprove;
    public string FormDataJson { get; set; } = "{}";
    public DateTime StartedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<HrFlowAction> Actions { get; set; } = new List<HrFlowAction>();
}

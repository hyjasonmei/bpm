namespace Bpm.Domain.Parallel;

public enum SlotDecision { Pending = 0, Approved = 1, Rejected = 2, Skipped = 3 }

/// <summary>
/// One branch of a parallel gateway = one approver's slot. Assignee is either a
/// specific user OR a role code (shared-role-queue: any holder of that role, or
/// their accepted delegate, may act). Skipped = the group already resolved
/// (threshold met or another slot rejected) so this slot no longer needs action
/// and drops out of the inbox.
/// </summary>
public class ParallelApprovalSlot
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }

    /// <summary>This branch's user-task node id (matches the BPMN diagram, for highlight).</summary>
    public string NodeId { get; set; } = string.Empty;

    public string? AssigneeRoleCode { get; set; }
    public Guid? AssigneeUserId { get; set; }

    public SlotDecision Decision { get; set; } = SlotDecision.Pending;
    public string? Comment { get; set; }
    public Guid? DecisionByUserId { get; set; }
    public DateTime? DecisionAt { get; set; }
}

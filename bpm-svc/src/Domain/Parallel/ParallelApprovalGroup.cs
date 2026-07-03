namespace Bpm.Domain.Parallel;

public enum ParallelGroupStatus { Open = 0, Approved = 1, Rejected = 2 }

/// <summary>
/// One parallel-approval gateway instance for one case. Shared runtime
/// primitive (not per-flow): any flow whose spec has a parallel gateway opens
/// a group on entering that step. Threshold M of N slots must approve to
/// resolve Approved; any single Rejected slot resolves the whole group
/// Rejected (v1 rule). Not a generic workflow engine — it only tracks the
/// slots + join for ONE parallel step; the owning flow's hand-written state
/// machine advances on the group result.
/// </summary>
public class ParallelApprovalGroup
{
    public Guid Id { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public int FlowVersion { get; set; }
    public Guid CaseId { get; set; }

    /// <summary>Spec fork-gateway node id (matches the BPMN diagram).</summary>
    public string GatewayNodeId { get; set; } = string.Empty;

    /// <summary>M in "M of N": N/N = AND (全簽), 1/N = OR (任一).</summary>
    public int Threshold { get; set; }

    /// <summary>N — total slots opened.</summary>
    public int TotalSlots { get; set; }

    public ParallelGroupStatus Status { get; set; } = ParallelGroupStatus.Open;
    public DateTime OpenedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public List<ParallelApprovalSlot> Slots { get; set; } = new();
}

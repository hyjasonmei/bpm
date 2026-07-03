using Bpm.Domain.Parallel;

namespace Bpm.Application.Parallel;

/// <summary>One branch to open: a target node id + its assignee (role OR user).</summary>
public sealed record SlotSpec(string NodeId, string? RoleCode, Guid? UserId);

/// <summary>A pending slot surfaced for the inbox, carrying its owning case.</summary>
public sealed record PendingSlot(Guid SlotId, string NodeId, Guid CaseId, string GatewayNodeId);

/// <summary>
/// Outcome of a decision: the group's post-decision status so the caller's flow
/// state machine can advance (Approved), reject the case (Rejected), or keep
/// waiting (Open). The full group (with slots) is returned for notification/display.
/// </summary>
public sealed record DecisionResult(ParallelGroupStatus GroupStatus, ParallelApprovalGroup Group);

/// <summary>
/// Shared primitive for one parallel-approval gateway step. Single
/// responsibility: track N slots + join/reject; it is NOT a generic workflow
/// engine. The owning flow opens a group, then advances its own state machine on
/// the <see cref="DecisionResult"/>.
/// </summary>
public interface IParallelApprovalService
{
    /// <summary>Open a gateway: create the group + one Pending slot per branch.</summary>
    Task<ParallelApprovalGroup> OpenAsync(
        string flowCode, int flowVersion, Guid caseId, string gatewayNodeId,
        IReadOnlyList<SlotSpec> slots, int threshold, CancellationToken ct);

    /// <summary>
    /// Record one approver's decision on their slot and recompute the group.
    /// Throws NotFoundException (slot), ConflictException (group resolved / slot
    /// decided), ForbiddenException (actor may not act on this slot).
    /// </summary>
    Task<DecisionResult> DecideAsync(
        Guid slotId, Guid actorUserId, bool approve, string? comment, CancellationToken ct);

    /// <summary>Load the group (+ slots) for a case's gateway, for display / inbox.</summary>
    Task<ParallelApprovalGroup?> GetAsync(Guid caseId, string gatewayNodeId, CancellationToken ct);

    /// <summary>Pending slots this user may act on (directly, or via one of their role codes),
    /// projected with their owning case id for inbox mapping.</summary>
    Task<IReadOnlyList<PendingSlot>> FindPendingForUserAsync(
        string flowCode, Guid userId, IReadOnlyCollection<string> roleCodes, CancellationToken ct);
}

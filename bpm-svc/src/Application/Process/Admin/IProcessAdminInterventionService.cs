using System.Text.Json;
using Bpm.Domain.Entities.Process;

namespace Bpm.Application.Process.Admin;

/// <summary>
/// PR-K4 §6.1 — admin-only "force" operations on running process instances.
/// These bypass the actor-identity / initiator checks that the regular
/// <see cref="Bpm.Application.Process.Runtime.IProcessRuntime"/> applies, in
/// exchange for a mandatory non-empty <c>reason</c> that lands in the
/// <see cref="TaskHistory.PayloadJson"/> alongside an
/// <c>actorRole = "admin"</c> marker. The audit trail is the contract: every
/// override leaves a row that explains who did what and why.
/// </summary>
public interface IProcessAdminInterventionService
{
    /// <summary>
    /// Reassign an open task to a new assignee. Resets <c>ClaimedAt</c> and
    /// drops the task back to <see cref="TaskStatus.Pending"/> — the new
    /// assignee starts from a clean slate.
    /// </summary>
    Task ForceReassignAsync(Guid taskId, Guid newAssigneeUserId, string reason, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Cancel the current task and spawn a fresh one at <paramref name="targetNodeId"/>.
    /// Unlike runtime ReturnTask which only walks back to the immediately
    /// preceding userTask, admin ForceReturn can jump to any node id present
    /// in the spec snapshot.
    /// </summary>
    Task ForceReturnAsync(Guid taskId, string targetNodeId, string reason, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Submit a task on behalf of the assignee. Reuses the regular runtime
    /// submit path (so gateways / approval-coordination logic runs unchanged)
    /// but skips the actor-must-be-assignee check.
    /// </summary>
    Task ForceSubmitAsync(Guid taskId, JsonElement? formDataPatch, Decision? decision, string reason, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Cancel a running instance regardless of who started it. Mirrors
    /// <see cref="Bpm.Application.Process.Runtime.IProcessRuntime.CancelInstanceAsync"/>
    /// but skips the initiator-only check.
    /// </summary>
    Task TerminateAsync(Guid instanceId, string reason, Guid actorUserId, CancellationToken ct = default);
}

using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

public class FlowLifecycleException : Exception
{
    public FlowLifecycleException(string message) : base(message) { }
}

/// <summary>A deployed flow's code + display name, for register-shipped.</summary>
public sealed record ShippedFlowInput(string FlowCode, string DisplayName);

/// <summary>Outcome of a register-shipped batch.</summary>
public sealed record RegisterShippedResult(IReadOnlyList<string> Registered, IReadOnlyList<string> Skipped);

public interface IFlowLifecycleService
{
    Task<Flow> CreateDraftAsync(string flowCode, string displayName, string? specJson, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Backfill: register flows whose runtime code is already deployed on
    /// bpm-svc (the <c>&lt;CODE&gt;_V1_Case</c> tables) but that never went
    /// through the AI Kitchen wizard, directly in <see cref="FlowState.Approved"/>
    /// so the bpm launcher lists them. Idempotent by FlowCode — an existing
    /// active (non-archived / non-retired / non-deleted) row is skipped.
    /// Bypasses the Draft → … → Committed cook lifecycle.
    /// </summary>
    Task<RegisterShippedResult> RegisterShippedAsync(IReadOnlyList<ShippedFlowInput> flows, Guid? actorUserId, CancellationToken ct = default);

    Task<Flow> UpdateSpecAsync(Guid flowId, string specJson, string? flowCode, string? displayName, Guid? actorUserId, CancellationToken ct = default);

    Task<Flow> SubmitAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CancelAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> ResumeAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CloneVersionAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> OnHoldFromChefAsync(Guid flowId, string question, CancellationToken ct = default);
    Task SoftDeleteDraftAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    /// <summary>Approved → Retired. Stops new case instances from
    /// being launched against this flow version.</summary>
    Task<Flow> RetireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    /// <summary>Retired → Approved. Brings a retired version back
    /// online (rare; usually you'd clone-as-new-version instead).</summary>
    Task<Flow> UnretireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    // ── chef-driven transitions (PR-K1) ──────────────────────────────
    // No actorUserId — chef sessions don't have a JWT sub. Heartbeat is
    // updated on every chef-side call to power the stall detector.

    /// <summary>Submitted → Cooking. Chef declares it picked up the spec.</summary>
    Task<Flow> ChefAcceptAsync(Guid flowId, CancellationToken ct = default);

    /// <summary>OnHold → Cooking. Chef self-resumes after fetching the user's reply.</summary>
    Task<Flow> ChefResumeAsync(Guid flowId, CancellationToken ct = default);

    /// <summary>Cooking → Committed. Chef declares the cook done.
    /// User still needs to flip Committed → Approved separately.</summary>
    Task<Flow> ChefCommitAsync(Guid flowId, CancellationToken ct = default);

    /// <summary>
    /// User-side ship-it: Committed → Approved. Called from the Serve
    /// tab; bpm launcher starts showing the flow after this. Gated to
    /// Committed only — admin can't approve directly from Cooking.
    /// </summary>
    Task<Flow> ApproveAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Approved → Published. Makes the flow live in this environment
    /// (the bpm-ui launcher only offers Published flows).</summary>
    Task<Flow> PublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Published → Approved. Takes the flow offline in this environment
    /// while keeping it reviewed.</summary>
    Task<Flow> UnpublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>User-triggered escape hatch when chef appears stalled.
    /// Cooking → Submitted (so a fresh chef session can pick up). Auth
    /// requires the user JWT; not exposed on the chef API surface.</summary>
    Task<Flow> ChefStallResetAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Pure heartbeat update — bump <c>LastChefHeartbeatAt</c>
    /// without changing state. Called by every read-only chef endpoint
    /// (GET flow / GET messages) so we still know chef is alive even
    /// when nothing transitioned.</summary>
    Task BumpChefHeartbeatAsync(Guid flowId, CancellationToken ct = default);

    /// <summary>
    /// Assign (or clear, with <c>groupId == null</c>) the launcher
    /// group on a flow. Admin Site Setting feature; never chef-driven.
    /// </summary>
    Task<Flow> AssignGroupAsync(Guid flowId, Guid? groupId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Set (or clear, with <c>iconKey == null</c>) the launcher icon on a
    /// flow. Display metadata for the bpm employee Create page; admin AI
    /// Kitchen flow-catalog panel. Never chef-driven.
    /// </summary>
    Task<Flow> SetIconAsync(Guid flowId, string? iconKey, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Write each flow's <c>DisplayOrder</c> to its position in
    /// <paramref name="orderedFlowIds"/>. Used by the drag-to-reorder
    /// flow-catalog panel. Ids not found are skipped silently.
    /// </summary>
    Task ReorderAsync(IReadOnlyList<Guid> orderedFlowIds, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Record chef's current worktree / branch context. Called by the
    /// chef MCP tool <c>chef_set_worktree</c>; admin UI surfaces it
    /// so the operator can find chef's in-flight code or relaunch the
    /// session in the right place. Pass <c>null</c> branch to clear.
    /// </summary>
    Task<Flow> SetChefWorkContextAsync(Guid flowId, string? branch, string? notes, CancellationToken ct = default);

    /// <summary>
    /// User-side reopen: Committed or Approved → OnHold so chef knows
    /// to revisit. Fired automatically when admin posts an Issue
    /// chat-reply; chef's next session sees state=OnHold + the message
    /// in the thread and resumes via <c>chef_transition('Resume')</c>.
    /// </summary>
    Task<Flow> ReopenForIssueAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
}

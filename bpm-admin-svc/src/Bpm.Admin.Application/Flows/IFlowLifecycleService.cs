using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

public class FlowLifecycleException : Exception
{
    public FlowLifecycleException(string message) : base(message) { }
}

/// <summary>A deployed flow's code + display name + max deployed runtime
/// version, for register-shipped. <paramref name="Version"/> defaults to 1 for
/// back-compat with callers that only know the code.</summary>
public sealed record ShippedFlowInput(string FlowCode, string DisplayName, int Version = 1);

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

    /// <summary>
    /// Rename the flow's display label only — allowed in ANY state (the
    /// display name is a label, not behaviour). Also syncs the spec's
    /// <c>meta.flowName</c> so the wizard title and launcher stay in step.
    /// Does NOT touch flowCode / spec semantics.
    /// </summary>
    Task<Flow> RenameAsync(Guid flowId, string displayName, Guid? actorUserId, CancellationToken ct = default);

    Task<Flow> SubmitAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CancelAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> ResumeAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CloneVersionAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> OnHoldFromChefAsync(Guid flowId, string question, CancellationToken ct = default);
    /// <summary>Soft-delete any pre-publish flow (everything except
    /// Published / Retired). Frees the FlowCode for reuse immediately —
    /// deleted rows drop out of the active-uniqueness checks via the
    /// soft-delete query filter.</summary>
    Task SoftDeleteAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
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

    /// <summary>Request the deploy: Approved (or PublishFailed for a retry) →
    /// Publishing. Gated on <see cref="Flow.MergedAt"/> (throws if not merged).
    /// Does NOT make the flow live — the flow only reaches Published once the
    /// deploy succeeds and <see cref="MarkPublishedAsync"/> fires.</summary>
    Task<Flow> PublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Publishing → Published. Records a successful deploy: sets
    /// <see cref="Flow.PublishedAt"/> and clears any prior failure reason.
    /// Only legal from Publishing.</summary>
    Task<Flow> MarkPublishedAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Publishing → PublishFailed. Records a failed deploy and stores
    /// <paramref name="reason"/> in <see cref="Flow.PublishFailedReason"/>.
    /// Only legal from Publishing; retry via <see cref="PublishAsync"/>.</summary>
    Task<Flow> MarkPublishFailedAsync(Guid flowId, string reason, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Published → Approved. Takes the flow offline in this environment
    /// while keeping it reviewed.</summary>
    Task<Flow> UnpublishAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>Record the PR the chef agent opened for this flow's cook
    /// branch (PR-CA1). Idempotent. Chef-token path (no actorUserId).</summary>
    Task<Flow> SetPrUrlAsync(Guid flowId, string prUrl, CancellationToken ct = default);

    /// <summary>Mark the cook branch merged into main; unblocks Publish
    /// (PR-CA1). Idempotent — a second call keeps the first timestamp.
    /// <paramref name="source"/> lands in the audit reason
    /// ("gh-pr" | "git-ancestry" | "manual").</summary>
    Task<Flow> MarkMergedAsync(Guid flowId, Guid? actorUserId, string source, CancellationToken ct = default);

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

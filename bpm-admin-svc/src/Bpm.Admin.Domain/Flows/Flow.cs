using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// One flow design row — corresponds to a single version of a flow lineage.
/// Multiple versions share a <see cref="LineageId"/>; editing an approved
/// version mints a new row with the next <see cref="Version"/>.
///
/// See openspec/specs/flowcook-lifecycle for the state machine.
/// </summary>
public class Flow : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Shared across every version of the same flow.</summary>
    public Guid LineageId { get; set; }

    /// <summary>1-based version number within a lineage.</summary>
    public int Version { get; set; }

    public FlowState State { get; set; }

    /// <summary>UPPERCASE short code (e.g. LEAVE, PURCHASE).</summary>
    public string FlowCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The full draft spec as JSON. Mutable while in Draft.</summary>
    public string SpecJson { get; set; } = "{}";

    /// <summary>Authoring notes. chef can append on-hold questions here.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Principal id of the user who created this version (nullable for system-seeded rows).</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Last time any chef API call touched this flow. Used by the
    /// admin UI to surface "chef stalled" when a flow has been in
    /// <see cref="FlowState.Cooking"/> for too long with no heartbeat.
    /// Updated by chef-token endpoints; null for flows that no chef has
    /// ever touched.
    /// </summary>
    public DateTime? LastChefHeartbeatAt { get; set; }

    /// <summary>
    /// Launcher group for the bpm employee Home page. Null means
    /// "unassigned" — the bpm-ui renders these under a built-in
    /// "其他" pseudo-group. Soft-deleting the referenced group
    /// leaves a dangling pointer here; bpm-svc filters via SharedFlowGroup
    /// (soft-delete query filter) so dangling rows surface as null.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Set when admin archives this flow's chef-cooked tables. Orthogonal
    /// to <see cref="State"/> and <see cref="DeletedAt"/>: admin can
    /// archive any state. Archived rows are hidden from the bpm-ui
    /// launcher even if their state would otherwise qualify, and their
    /// FlowCode no longer blocks a new Submit (the active-uniqueness
    /// check ignores archived rows).
    /// </summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// JSON-encoded list of the renamed table names produced by the
    /// archive operation. Used by Restore to find them back.
    /// Format: <c>["LEAVE_V1_leave_case__arch_a3c7f1e2", …]</c>.
    /// </summary>
    public string? ArchivedTableNamesJson { get; set; }

    /// <summary>
    /// Free-form JSON about chef's current workspace. Today shape:
    /// <c>{ "branch": "leave-test-6", "notes": "Domain layer done", "setAt": "2026-05-29T01:00:00Z" }</c>.
    /// Surfaced in admin-ui CookPanel + list page so the operator can
    /// find chef's in-flight code or relaunch session in the right
    /// worktree. Updated by the chef MCP tool <c>chef_set_worktree</c>.
    /// </summary>
    public string? ChefWorkContextJson { get; set; }
}

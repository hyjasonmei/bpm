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
}

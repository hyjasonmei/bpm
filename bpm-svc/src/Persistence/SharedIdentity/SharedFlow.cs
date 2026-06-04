namespace Bpm.Persistence.SharedIdentity;

/// <summary>
/// Read-only mapping onto Admin_Flows owned by bpm-admin-svc. Lets
/// bpm-svc tell which cooked flow versions are currently Approved
/// (eligible for new case instances) vs Retired (existing cases keep
/// going, no new launches). bpm-svc never writes this row — admin-svc
/// owns the lifecycle. EF migration is ExcludeFromMigrations because
/// admin-svc creates the schema.
/// </summary>
public class SharedFlow
{
    public Guid Id { get; set; }
    public Guid LineageId { get; set; }
    public int Version { get; set; }

    /// <summary>Enum stored as int — values match Bpm.Admin.Domain.Flows.FlowState.</summary>
    public SharedFlowState State { get; set; }

    public string FlowCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>FK onto <see cref="SharedFlowGroup.Id"/>; null when unassigned.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Mirror of <c>Admin_Flows.IconKey</c> — curated lucide icon name for
    /// the bpm launcher tile. Null falls back to a default icon client-side.
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// Mirror of <c>Admin_Flows.DisplayOrder</c> — launcher sort weight
    /// within the flow's group (low → high; ties break on FlowCode).
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Mirror of <c>Admin_Flows.ArchivedAt</c>. Set by admin's Feature
    /// Tables tab when the per-flow EF tables are renamed away
    /// (<c>__arch_&lt;hash&gt;</c>). bpm-svc reads this to hide the flow
    /// from <c>/api/flow-registry</c> (and downstream bpm-ui Home /
    /// inbox) — once the backing table is gone, the flow is dead
    /// regardless of whether <see cref="State"/> is Approved or Retired.
    /// </summary>
    public DateTime? ArchivedAt { get; set; }
}

/// <summary>
/// Mirrors Bpm.Admin.Domain.Flows.FlowState — kept here to avoid
/// cross-service domain references. Values must stay in sync with the
/// admin enum.
/// </summary>
public enum SharedFlowState
{
    Draft = 0,
    Submitted = 1,
    Cooking = 2,
    OnHold = 3,
    Committed = 4,
    Approved = 5,
    Rejected = 6,
    Retired = 7,
}

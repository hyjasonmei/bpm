namespace Bpm.Persistence.SharedIdentity;

/// <summary>
/// Read-only mapping onto Admin_FlowGroups owned by bpm-admin-svc.
/// bpm-svc joins this against <see cref="SharedFlow"/> when serving
/// the flow-registry endpoint so end-user UIs can render grouped
/// launchers without a second admin-svc round-trip. Schema owned by
/// admin (ExcludeFromMigrations).
/// </summary>
public class SharedFlowGroup
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>JSON-encoded bilingual label — admin writes via FlowGroupService.</summary>
    public string DisplayNameJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public string? Icon { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

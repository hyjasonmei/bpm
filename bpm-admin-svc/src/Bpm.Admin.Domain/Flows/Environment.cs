using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// A deployment target the admin user tracks against — DEV / STG / PRD
/// for typical setups, but the table is customer-editable so other
/// tiers (UAT, BCP, etc.) can be added at any time. POC scope: this
/// is just a checkbox shape; no automation actually deploys.
/// </summary>
public class Environment : ISoftDeletable
{
    public Guid Id { get; set; }
    /// <summary>Stable slug, e.g. "dev" / "stg" / "prd". Unique among live rows.</summary>
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Lower wins (DEV before STG before PRD by default).</summary>
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

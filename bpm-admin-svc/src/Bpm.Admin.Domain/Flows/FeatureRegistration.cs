using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// Chef registers the list of physical tables it created for a flow
/// version here, so admin can identify (and later archive / drop)
/// them by Flow id without guessing from naming patterns. Today this
/// table is populated retroactively by Site Setting → Feature Tables
/// when admin clicks Archive on a discovered flow; a future chef MCP
/// tool will push the row at the end of a successful cook.
/// </summary>
public class FeatureRegistration : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Owning admin flow; null for orphan registrations.</summary>
    public Guid? FlowId { get; set; }

    public string FlowCode { get; set; } = string.Empty;
    public int Version { get; set; }

    /// <summary>JSON array of physical table names (current, not archived).</summary>
    public string TableNamesJson { get; set; } = "[]";

    public DateTime RegisteredAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// One launcher group on the bpm employee Home page. Cooked flows
/// reference it through <see cref="Flow.GroupId"/>; bpm-ui groups its
/// QuickActionsPanel by code, ordered by <see cref="SortOrder"/>.
/// Unassigned flows fall into a pseudo-group "其他" on the client.
/// </summary>
public class FlowGroup : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable slug — bpm-ui groups by this, so it must stay unique
    /// across the live (non-deleted) set. Lowercase ASCII; admin UI
    /// enforces on input.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Bilingual label, JSON-encoded:
    /// <c>{ "zh-TW": "人事", "en": "HR" }</c>. zh-TW is required; en
    /// is optional. Stored as a string so SQLite + EF stay simple
    /// (no Owned-types dance) — the admin layer parses on read.
    /// </summary>
    public string DisplayNameJson { get; set; } = "{}";

    /// <summary>
    /// Lower wins — bpm-ui orders sections ascending. Admin can drag
    /// to re-sort; SortOrder is rewritten on every drop.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// lucide-react icon name (e.g. <c>Users</c>, <c>ShoppingCart</c>).
    /// Stored as a string so the schema doesn't get tangled with the
    /// frontend's icon catalog; bpm-ui maps name → component on render
    /// with a generic <c>Folder</c> fallback for unknown names.
    /// </summary>
    public string? Icon { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

namespace Bpm.Application.Inbox;

/// <summary>
/// One row of the unified inbox: a per-flow case projected into a shape
/// the bpm-ui Home page can render generically. Chef-cooked features
/// provide their own <see cref="ITypedInboxProvider"/> that builds these
/// from per-flow tables; <c>InboxController</c> fans out across every
/// registered provider and merges by <see cref="LastActivityAt"/>.
/// </summary>
/// <param name="CaseId">Per-flow case identifier (matches the chef table PK).</param>
/// <param name="FlowCode">Spec-defined flow code, e.g. "LEAVE".</param>
/// <param name="FlowVersion">Spec version, e.g. 1.</param>
/// <param name="Title">Human-readable summary for the inbox row (e.g. "Bob 申請 特休 3 天").</param>
/// <param name="Status">Per-flow status string (display only — not parsed by UI).</param>
/// <param name="Lifecycle">
/// Canonical, UI-parseable lifecycle state (one of <see cref="InboxLifecycle"/>:
/// Open / Completed / Cancelled / Rejected). The dashboard counts on THIS —
/// never on <see cref="Status"/>, which is per-flow localized display text.
/// </param>
/// <param name="SubmittedAt">When the case was first created.</param>
/// <param name="LastActivityAt">When the case last changed state. Used for sorting.</param>
/// <param name="DetailUrl">
/// Relative URL the UI should route to on row click. Owned by the feature
/// (chef decides the per-flow case-detail page path).
/// </param>
public sealed record InboxRow(
    Guid CaseId,
    string FlowCode,
    int FlowVersion,
    string Title,
    string Status,
    string Lifecycle,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    string DetailUrl);

/// <summary>
/// Canonical lifecycle buckets for the unified inbox — the UI-parseable
/// counterpart to the per-flow localized <see cref="InboxRow.Status"/>.
/// Every flow's status enum names its terminals identically
/// (<c>Completed</c> / <c>Cancelled</c>, plus <c>Rejected</c> where a flow has
/// a terminal reject); every other state (Pending*, ResubmitRequired) is Open.
/// So the mapping is by enum-member name via <see cref="FromStatusName"/> —
/// each provider passes <c>InboxLifecycle.FromStatusName(c.Status.ToString())</c>.
/// </summary>
public static class InboxLifecycle
{
    public const string Open = "Open";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string Rejected = "Rejected";

    /// <summary>Map a per-flow status enum's member name to a canonical bucket.</summary>
    public static string FromStatusName(string statusName) => statusName switch
    {
        "Completed" => Completed,
        "Cancelled" => Cancelled,
        "Rejected"  => Rejected,
        _           => Open,
    };
}

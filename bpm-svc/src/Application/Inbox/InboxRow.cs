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
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    string DetailUrl);

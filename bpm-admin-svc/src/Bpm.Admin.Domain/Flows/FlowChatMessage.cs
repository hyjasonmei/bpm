using Bpm.Admin.Domain.SoftDelete;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// One message on a flow's Cook-tab timeline. Two-way conversation
/// between the admin user and a chef session — chef writes memos /
/// questions / completions, user replies / opens issues. The Cook tab
/// in admin-ui renders this thread in chronological order.
/// </summary>
public class FlowChatMessage : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>Flow this message belongs to.</summary>
    public Guid FlowId { get; set; }

    public FlowChatSender Sender { get; set; }
    public FlowChatKind Kind { get; set; }

    /// <summary>Rendered markdown — Cook tab uses ReactMarkdown.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Free-form JSON. chef posts e.g.
    /// <c>{ "branch": "leave-test-6", "fileCount": 14, "testsPassing": 23 }</c>.
    /// Persisted as a string so EF / SQLite don't need an Owned-types
    /// dance; admin-ui parses on render.
    /// </summary>
    public string? ArtifactsJson { get; set; }

    /// <summary>Only set on completion rows (e.g. "V1.0", "V1.1").</summary>
    public string? Version { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Null for chef / system rows; carries the JWT sub for user rows.</summary>
    public Guid? AuthorUserId { get; set; }

    public DateTime? DeletedAt { get; set; }
}

public enum FlowChatSender
{
    User = 0,
    Chef = 1,
    System = 2,
}

public enum FlowChatKind
{
    /// <summary>chef → progress update ("Domain layer done").</summary>
    Memo = 0,
    /// <summary>chef → blocker; pairs with transition to OnHold.</summary>
    Question = 1,
    /// <summary>chef → "cook done", artifactsJson populated.</summary>
    Completion = 2,
    /// <summary>user → reply to a chef question (while OnHold).</summary>
    Reply = 3,
    /// <summary>user → opens an issue after Committed.</summary>
    Issue = 4,
    /// <summary>Auto-generated state-transition note (no human author).</summary>
    System = 5,
}

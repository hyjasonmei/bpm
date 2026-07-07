using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Support;

/// An in-app problem report ("Report an issue" in the Help menu). Captured
/// with the reporter's identity and browser context so support can follow up
/// without a back-and-forth.
public sealed class SupportIssue : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    /// bug | feature | question — free string so new kinds don't need a migration.
    public string Kind { get; set; } = "bug";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Contact { get; set; }
    /// Route the reporter was on when they opened the dialog.
    public string? Page { get; set; }
    public string? UserAgent { get; set; }
    public SupportIssueStatus Status { get; set; } = SupportIssueStatus.New;
    public DateTime SubmittedAt { get; set; }
}

public enum SupportIssueStatus
{
    New = 1,
    Acknowledged = 2,
    Closed = 3,
}

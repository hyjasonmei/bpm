namespace Bpm.Domain.Entities.Notifications;

/// <summary>
/// A single in-app notification destined for one user — the row behind
/// the header 🔔 bell. Written by the in-app notify dispatcher whenever a
/// flow dispatches a notification whose channels include <c>in_app</c>
/// and whose recipient resolved to a concrete user id. Read-state is
/// per-row so the bell can show an unread count and "mark as read".
/// </summary>
public sealed class UserNotification
{
    public Guid Id { get; set; }

    /// <summary>Resolved recipient (the user who sees it in their bell).</summary>
    public Guid UserId { get; set; }

    /// <summary>Notification subject — the bold line in the bell list.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Rendered body text.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>In-app deep link (e.g. <c>/cases/ape/{caseId}</c>); null when
    /// the source notification carried no case context.</summary>
    public string? Link { get; set; }

    /// <summary>Originating <see cref="Bpm.Domain"/>-side source id
    /// (NotifyMessage.SourceId) for traceability.</summary>
    public string? SourceId { get; set; }

    /// <summary>Flow code from the notification context, for the type chip.</summary>
    public string? FlowCode { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

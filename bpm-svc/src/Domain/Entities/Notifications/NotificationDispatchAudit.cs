namespace Bpm.Domain.Entities.Notifications;

/// <summary>
/// One row per notification fired by <c>INotificationDispatcher</c>.
/// Append-only — the table is the source-of-truth for "what
/// notification went out, when, to whom" in production. The MVP
/// dispatcher (sandbox-capturing) writes it; a real notification
/// engine (future <c>add-notification-engine</c> work) keeps the
/// same shape so the audit trail is continuous across the swap.
///
/// Status semantics:
///   - <c>captured</c> — sandbox-on; nothing left the building, the
///     row is a record of "would have been sent".
///   - <c>dispatched</c> — sandbox-off; the inner dispatcher
///     succeeded (today: log-only; later: real engine).
///   - <c>failed</c> — inner dispatcher threw; <see cref="Error"/>
///     carries the message.
/// </summary>
public sealed class NotificationDispatchAudit
{
    public Guid Id { get; set; }
    public Guid InstanceId { get; set; }
    public Guid? TaskId { get; set; }
    public required string SpecCode { get; set; }
    public required string Trigger { get; set; }
    public required string NotificationId { get; set; }
    public string? Channel { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public required string Status { get; set; }
    public string? Error { get; set; }
    public DateTime DispatchedAt { get; set; }
}

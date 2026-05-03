namespace Bpm.Application.Common.Notifications;

public sealed record NotificationMessage(
    string Trigger,
    IReadOnlyCollection<string> Channels,
    IReadOnlyCollection<string> Recipients,
    string Subject,
    string Body
);

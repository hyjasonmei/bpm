using Bpm.Application.Common.Notifications;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// Phase A INotificationSender: logs the notification message. Channels are
/// not actually delivered — this is a stand-in until SMTP / Teams hooks land.
public sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        logger.LogInformation(
            "NOTIFY trigger={Trigger} channels=[{Channels}] to=[{Recipients}] subject={Subject}\n{Body}",
            message.Trigger,
            string.Join(",", message.Channels),
            string.Join(",", message.Recipients),
            message.Subject,
            message.Body);
        return Task.CompletedTask;
    }
}

namespace Bpm.Application.Common.Notifications;

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken ct = default);
}

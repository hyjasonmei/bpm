using Bpm.Application.Common.Notifications;

namespace Bpm.Tests.Common;

internal sealed class CapturingNotificationSender : INotificationSender
{
    public List<NotificationMessage> Sent { get; } = new();

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

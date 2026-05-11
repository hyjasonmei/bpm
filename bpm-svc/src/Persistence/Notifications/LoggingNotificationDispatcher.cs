using Bpm.Application.Notifications;
using Bpm.Application.Process.Runtime;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// <summary>
/// V1 dispatcher: enumerates matching notifications in the snapshot and logs
/// the dispatch. No NotificationDelivery rows are inserted (that table is
/// owned by <c>add-notification-engine</c>).
/// </summary>
// TODO(add-notification-engine): replace with a dispatcher that renders
// templates and inserts NotificationDelivery rows for the worker to pick up.
public sealed class LoggingNotificationDispatcher(ILogger<LoggingNotificationDispatcher> logger) : INotificationDispatcher
{
    public Task DispatchAsync(SpecSnapshot snapshot, string trigger, NotificationContext ctx, CancellationToken ct = default)
    {
        var matches = snapshot.GetNotifications(trigger);
        logger.LogInformation(
            "Notification dispatch (stub): trigger={Trigger} instance={Instance} spec={Spec} matchedSpecs={Count}",
            trigger, ctx.InstanceId, ctx.SpecCode, matches.Count);
        return Task.CompletedTask;
    }
}

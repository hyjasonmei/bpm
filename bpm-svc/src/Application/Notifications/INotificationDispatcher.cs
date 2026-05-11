using Bpm.Application.Process.Runtime;

namespace Bpm.Application.Notifications;

/// <summary>
/// Invoked by the runtime when a state-change event matches a notification's
/// trigger (<c>on_submit</c>, <c>on_assign</c>, <c>on_approve</c>, <c>on_reject</c>,
/// <c>on_complete</c>, ...). The dispatcher is responsible for selecting matching
/// notification specs from the snapshot, rendering templates, and recording
/// delivery rows. V1 implementation merely logs.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(SpecSnapshot snapshot, string trigger, NotificationContext ctx, CancellationToken ct = default);
}

public sealed record NotificationContext(
    Guid InstanceId,
    string SpecCode,
    Guid InitiatorUserId,
    IReadOnlyDictionary<string, object?> FormData,
    IReadOnlySet<Guid>? CurrentApprovers,
    IReadOnlySet<Guid>? CurrentAssignees);

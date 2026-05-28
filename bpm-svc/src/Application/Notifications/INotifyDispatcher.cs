namespace Bpm.Application.Notifications;

/// <summary>
/// Model-B (per-flow, chef-cooked) notification sink. Chef-cooked
/// services call this after a state-machine transition lands; the
/// dispatcher is responsible for fanning the rendered subject + body
/// out to whatever channel the deployment has wired up.
///
/// Distinct from the legacy <c>INotificationDispatcher</c> in this
/// same namespace, which belongs to the retired Model-A runtime
/// (<c>SpecSnapshot</c> driven). New code MUST target this interface.
/// </summary>
public interface INotifyDispatcher
{
    Task DispatchAsync(NotifyMessage message, CancellationToken ct = default);
}

/// <summary>
/// One rendered notification ready to leave the runtime. Templates have
/// already been substituted; recipients have already been resolved to
/// principals / emails. The dispatcher only needs to deliver.
/// </summary>
/// <param name="SourceId">
/// Stable identifier from the spec — usually
/// <c>{FlowCode}_{Version}.{notificationId}</c> (e.g.
/// <c>LEAVE_V1.notify_assign_manager</c>). Used for audit / dedup.
/// </param>
/// <param name="Subject">Rendered subject line.</param>
/// <param name="Body">Rendered body (already line-broken).</param>
/// <param name="Channels">
/// Channel hints from the spec (<c>email</c>, <c>in_app</c>,
/// <c>teams</c>). The dispatcher decides which channels it actually
/// supports — file dispatcher writes one entry regardless.
/// </param>
/// <param name="Recipients">Resolved targets. May be empty.</param>
/// <param name="Context">Optional tracing keys — case id, actor user
/// id, instance id, anything else useful in audit.</param>
public sealed record NotifyMessage(
    string SourceId,
    string Subject,
    string Body,
    IReadOnlyList<string> Channels,
    IReadOnlyList<NotifyRecipient> Recipients,
    IReadOnlyDictionary<string, string?>? Context = null);

public sealed record NotifyRecipient(Guid? UserId, string? Email, string? DisplayName);

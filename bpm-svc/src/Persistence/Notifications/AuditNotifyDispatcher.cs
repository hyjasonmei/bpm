using Bpm.Application.Common.Abstractions;
using Bpm.Application.Notifications;
using Bpm.Domain.Entities.Notifications;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// <summary>
/// Production notification audit sink — writes one <see cref="NotificationDispatchAudit"/>
/// row per dispatched notification ("what went out, when, to whom"). This is
/// the writer the audit table was always meant to have; it runs in every
/// environment (the sandbox-capture sink remains separate, for UAT mailbox
/// preview). Best-effort: an audit failure is logged, never propagated.
/// </summary>
public sealed class AuditNotifyDispatcher(
    AppDbContext db,
    IClock clock,
    ILogger<AuditNotifyDispatcher> log) : INotifyDispatcher
{
    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        var ctx = message.Context ?? new Dictionary<string, string?>();
        Guid.TryParse(Get(ctx, "caseId"), out var caseId);

        var audit = new NotificationDispatchAudit
        {
            Id = Guid.NewGuid(),
            InstanceId = caseId,
            TaskId = null,
            SpecCode = Get(ctx, "flowCode") ?? "?",
            Trigger = Get(ctx, "event") ?? Get(ctx, "stage") ?? "dispatch",
            NotificationId = message.SourceId,
            Channel = message.Channels.Count > 0 ? string.Join(",", message.Channels) : null,
            Recipient = FormatRecipients(message.Recipients),
            Subject = message.Subject,
            Body = message.Body,
            Status = "dispatched",
            DispatchedAt = clock.UtcNow,
        };

        try
        {
            db.Set<NotificationDispatchAudit>().Add(audit);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Notification audit write failed for {SourceId}", message.SourceId);
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string?> ctx, string key)
        => ctx.TryGetValue(key, out var v) ? v : null;

    private static string? FormatRecipients(IReadOnlyList<NotifyRecipient> recipients)
    {
        if (recipients.Count == 0) return null;
        return string.Join("; ", recipients.Select(r =>
            r.Email is not null ? $"{r.DisplayName ?? r.Email} <{r.Email}>" : r.UserId?.ToString() ?? "(unknown)"));
    }
}

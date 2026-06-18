using Bpm.Application.Common.Abstractions;
using Bpm.Application.Notifications;
using Bpm.Domain.Entities.Notifications;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// <summary>
/// Persists in-app notifications to <see cref="UserNotification"/> so the
/// header bell can surface them. Only acts on messages whose channels
/// include <c>in_app</c>; writes one row per recipient that resolved to a
/// concrete user id (email-only recipients are skipped — they go out via
/// the email sink instead). The deep link is derived from the message
/// context (<c>flowCode</c> + <c>caseId</c>) using the same lower-snake
/// slug the bpm-ui <c>/cases/:flowCode/:caseId</c> route resolves.
/// </summary>
public sealed class InAppNotifyDispatcher(
    AppDbContext db,
    IClock clock,
    ILogger<InAppNotifyDispatcher> log) : INotifyDispatcher
{
    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        if (!message.Channels.Contains("in_app")) return;

        var link = BuildLink(message.Context);
        var flowCode = message.Context?.GetValueOrDefault("flowCode");
        var now = clock.UtcNow;

        var added = 0;
        foreach (var r in message.Recipients)
        {
            if (r.UserId is not { } userId) continue;
            db.Set<UserNotification>().Add(new UserNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = message.Subject,
                Body = message.Body,
                Link = link,
                SourceId = message.SourceId,
                FlowCode = flowCode,
                IsRead = false,
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            log.LogInformation("Notify → bell: {SourceId} → {Count} recipient(s)", message.SourceId, added);
        }
    }

    private static string? BuildLink(IReadOnlyDictionary<string, string?>? ctx)
    {
        if (ctx is null) return null;
        var caseId = ctx.GetValueOrDefault("caseId");
        var flowCode = ctx.GetValueOrDefault("flowCode");
        if (string.IsNullOrEmpty(caseId) || string.IsNullOrEmpty(flowCode)) return null;
        var slug = flowCode.ToLowerInvariant();
        // Qualify with the flow version (chef ships it in the notify context) so a
        // bell link to an OLD-version case opens THAT version's detail page via
        // bpm-ui's /cases/:flowCode/v:version/:caseId route. Without the version
        // the link resolves to the latest version's detail, which fetches a
        // sibling-version endpoint and 404s once a newer version is published.
        var flowVersion = ctx.GetValueOrDefault("flowVersion");
        return string.IsNullOrEmpty(flowVersion)
            ? $"/cases/{slug}/{caseId}"
            : $"/cases/{slug}/v{flowVersion}/{caseId}";
    }
}

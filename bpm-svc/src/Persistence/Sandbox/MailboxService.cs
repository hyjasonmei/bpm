using System.Text.Json;
using Bpm.Application.Sandbox;
using Bpm.Application.Sandbox.Dtos;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// PR-J4 §7 implementation. Filters use <see cref="EF.Functions.Like"/> over
/// the JSON text columns (<c>ReadByUserIdsJson</c>, <c>IntendedRecipientsJson</c>)
/// so the same query compiles to <c>LIKE</c> on both SQLite and Postgres —
/// per the project DB conventions. A real JSON-aware filter ships with
/// <c>add-real-search</c>; v1 acceptable.
/// </summary>
public sealed class MailboxService(
    AppDbContext db,
    ISandboxClockService clockService) : IMailboxService
{
    private const string DefaultTenant = "default";
    private const int MaxLimit = 200;
    private const int DefaultLimit = 50;

    public async Task<IReadOnlyList<CapturedMessageSummaryDto>> ListAsync(
        Guid currentUserId,
        SandboxChannel? channel,
        Guid? recipientUserIdHint,
        Guid? processInstanceId,
        bool unreadOnly,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = DefaultLimit;
        if (limit > MaxLimit) limit = MaxLimit;

        var q = db.SandboxCapturedMessages.AsNoTracking()
            .Where(m => m.TenantCode == DefaultTenant);

        if (channel.HasValue)
            q = q.Where(m => m.Channel == channel.Value);
        if (processInstanceId.HasValue)
            q = q.Where(m => m.ProcessInstanceId == processInstanceId.Value);
        if (recipientUserIdHint.HasValue)
        {
            // Substring match against the JSON array text column. Looks for
            // the recipient guid anywhere inside IntendedRecipientsJson — good
            // enough for the v1 "did this user receive it?" filter.
            var token = recipientUserIdHint.Value.ToString();
            q = q.Where(m => EF.Functions.Like(m.IntendedRecipientsJson, "%" + token + "%"));
        }
        if (unreadOnly)
        {
            var userToken = currentUserId.ToString();
            q = q.Where(m => !EF.Functions.Like(m.ReadByUserIdsJson, "%" + userToken + "%"));
        }

        var rows = await q
            .OrderByDescending(m => m.CapturedAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.ProcessInstanceId,
                m.TaskId,
                m.Channel,
                m.Subject,
                m.EventType,
                m.CapturedAt,
                m.ReadByUserIdsJson,
            })
            .ToListAsync(ct);

        var currentToken = currentUserId.ToString();
        return rows.Select(r => new CapturedMessageSummaryDto(
            r.Id, r.ProcessInstanceId, r.TaskId, r.Channel, r.Subject, r.EventType,
            r.CapturedAt,
            ReadByMe: ContainsUserId(r.ReadByUserIdsJson, currentToken))).ToList();
    }

    public async Task<CapturedMessageDetailDto?> GetAsync(Guid id, Guid currentUserId, CancellationToken ct = default)
    {
        var m = await db.SandboxCapturedMessages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantCode == DefaultTenant, ct);
        if (m is null) return null;

        IReadOnlyList<string> recipients = Array.Empty<string>();
        try
        {
            recipients = JsonSerializer.Deserialize<List<string>>(m.IntendedRecipientsJson) ?? new();
        }
        catch { /* tolerate malformed legacy rows */ }

        var readByMe = ContainsUserId(m.ReadByUserIdsJson, currentUserId.ToString());
        return new CapturedMessageDetailDto(
            m.Id, m.ProcessInstanceId, m.TaskId, m.Channel,
            recipients, m.Subject, m.BodyHtml, m.BodyText,
            m.Url, m.HeadersJson, m.PayloadJson, m.EventType, m.Body,
            m.CapturedAt, readByMe,
            m.OriginatingNotificationId, m.OriginatingWebhookSubscriptionId);
    }

    public async Task<bool> MarkReadAsync(Guid id, Guid currentUserId, CancellationToken ct = default)
    {
        var row = await db.SandboxCapturedMessages
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantCode == DefaultTenant, ct);
        if (row is null) return false;

        List<string> readers;
        try
        {
            readers = JsonSerializer.Deserialize<List<string>>(row.ReadByUserIdsJson) ?? new();
        }
        catch
        {
            readers = new();
        }

        var token = currentUserId.ToString();
        if (readers.Contains(token)) return true;  // idempotent

        readers.Add(token);
        row.ReadByUserIdsJson = JsonSerializer.Serialize(readers);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<UnreadCountDto> UnreadCountAsync(Guid currentUserId, CancellationToken ct = default)
    {
        // Spec §7.5: when sandbox is off, return zero counts WITHOUT querying
        // the DB. Keeps the prod /unread-count poll cost near-zero.
        var clock = await clockService.GetAsync(ct);
        var emptyByChannel = new Dictionary<string, int>
        {
            [nameof(SandboxChannel.Email)] = 0,
            [nameof(SandboxChannel.Webhook)] = 0,
            [nameof(SandboxChannel.Sms)] = 0,
        };
        if (!clock.SandboxOn)
            return new UnreadCountDto(0, emptyByChannel);

        var userToken = currentUserId.ToString();
        var rows = await db.SandboxCapturedMessages.AsNoTracking()
            .Where(m => m.TenantCode == DefaultTenant
                && !EF.Functions.Like(m.ReadByUserIdsJson, "%" + userToken + "%"))
            .GroupBy(m => m.Channel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var row in rows)
            emptyByChannel[row.Channel.ToString()] = row.Count;

        return new UnreadCountDto(rows.Sum(r => r.Count), emptyByChannel);
    }

    private static bool ContainsUserId(string? json, string token)
    {
        if (string.IsNullOrEmpty(json)) return false;
        return json.Contains(token, StringComparison.Ordinal);
    }
}

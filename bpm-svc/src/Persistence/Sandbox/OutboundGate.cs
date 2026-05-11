using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// Sandbox outbound gate. When sandbox mode is on, the default behaviour is
/// to <b>capture</b> the full payload into <see cref="SandboxCapturedMessage"/>
/// (returning <see cref="GateOutcome{T}.Capture"/>) so the Mailbox UI can
/// render exactly what the recipient would have seen in production. The
/// PR-J0 "rewrite to alt recipients / drop" path remains available as opt-in
/// via <c>SandboxConfigDto.LegacyRewriteEnabled = true</c>; in that mode the
/// gate ALSO writes the new capture row so the Mailbox stays consistent.
/// </summary>
/// <remarks>
/// Captured outcomes are the gate's "fake 200 OK" contract: downstream
/// dispatchers (mail relay, webhook poster, SMS gateway) MUST treat a
/// captured outcome as success and skip the actual network call.
/// </remarks>
public sealed class OutboundGate(AppDbContext db, IClock clock) : IOutboundGate
{
    private const string DefaultTenant = "default";

    public async Task<GateOutcome<EmailMessage>> ApplyAsync(EmailMessage msg, CancellationToken ct = default)
    {
        var (enabled, config) = await LoadAsync(ct);
        if (!enabled) return GateOutcome<EmailMessage>.PassThrough(msg);

        var originals = msg.To.Concat(msg.Cc).Concat(msg.Bcc).ToList();

        // Default capture-only path. Always writes the full payload first so the
        // Mailbox sees it even when the legacy rewrite would have dropped.
        var captured = await CaptureEmailAsync(msg, originals, ct);

        if (!(config?.LegacyRewriteEnabled ?? false))
        {
            return GateOutcome<EmailMessage>.Capture(captured.Id);
        }

        // Legacy opt-in: rewrite or drop, plus the old SandboxRedirect audit row.
        var recipients = config?.EmailRecipients ?? new List<string>();
        if (recipients.Count == 0)
        {
            await WriteAudit(SandboxChannel.Email, SandboxAction.Dropped, originals, recipients, msg.Subject, ct);
            return GateOutcome<EmailMessage>.DropMessage();
        }

        var banner = $"[SANDBOX MODE] Original recipients: {string.Join(", ", originals)}\nDispatched at {clock.UtcNow:O}\n----\n";
        var bannerHtml = $"<div style=\"border:2px solid #f59e0b;background:#fef3c7;padding:12px;margin-bottom:16px;font-family:monospace;\">[SANDBOX MODE] Original recipients: {string.Join(", ", originals)}<br/>Dispatched at {clock.UtcNow:O}</div>";
        var rewritten = msg with
        {
            To = recipients.ToList(),
            Cc = new List<string>(),
            Bcc = new List<string>(),
            BodyHtml = bannerHtml + msg.BodyHtml,
            BodyText = banner + msg.BodyText,
        };
        await WriteAudit(SandboxChannel.Email, SandboxAction.Redirected, originals, recipients.ToList(), msg.Subject, ct);
        return GateOutcome<EmailMessage>.Rewrote(rewritten);
    }

    public async Task<GateOutcome<WebhookDelivery>> ApplyAsync(WebhookDelivery msg, CancellationToken ct = default)
    {
        var (enabled, config) = await LoadAsync(ct);
        if (!enabled) return GateOutcome<WebhookDelivery>.PassThrough(msg);

        var captured = await CaptureWebhookAsync(msg, ct);

        if (!(config?.LegacyRewriteEnabled ?? false))
        {
            return GateOutcome<WebhookDelivery>.Capture(captured.Id);
        }

        if (string.IsNullOrWhiteSpace(config?.WebhookUrl))
        {
            await WriteAudit(SandboxChannel.Webhook, SandboxAction.Dropped, new List<string> { msg.Url }, new List<string>(), msg.EventType, ct);
            return GateOutcome<WebhookDelivery>.DropMessage();
        }

        var hdrs = new Dictionary<string, string>(msg.Headers)
        {
            ["X-BPM-Sandbox-Original-Url"] = msg.Url,
        };
        var rewritten = msg with { Url = config.WebhookUrl, Headers = hdrs };
        await WriteAudit(SandboxChannel.Webhook, SandboxAction.Redirected, new List<string> { msg.Url }, new List<string> { config.WebhookUrl }, msg.EventType, ct);
        return GateOutcome<WebhookDelivery>.Rewrote(rewritten);
    }

    public async Task<GateOutcome<SmsMessage>> ApplyAsync(SmsMessage msg, CancellationToken ct = default)
    {
        var (enabled, config) = await LoadAsync(ct);
        if (!enabled) return GateOutcome<SmsMessage>.PassThrough(msg);

        var captured = await CaptureSmsAsync(msg, ct);

        if (!(config?.LegacyRewriteEnabled ?? false))
        {
            return GateOutcome<SmsMessage>.Capture(captured.Id);
        }

        var recipients = config?.SmsRecipients ?? new List<string>();
        if (recipients.Count == 0)
        {
            await WriteAudit(SandboxChannel.Sms, SandboxAction.Dropped, msg.To.ToList(), recipients, Truncate(msg.Body, 80), ct);
            return GateOutcome<SmsMessage>.DropMessage();
        }
        var prefixed = msg with
        {
            To = recipients.ToList(),
            Body = $"[SANDBOX → originally to: {string.Join(", ", msg.To)}] {msg.Body}",
        };
        await WriteAudit(SandboxChannel.Sms, SandboxAction.Redirected, msg.To.ToList(), recipients.ToList(), Truncate(msg.Body, 80), ct);
        return GateOutcome<SmsMessage>.Rewrote(prefixed);
    }

    private async Task<SandboxCapturedMessage> CaptureEmailAsync(EmailMessage msg, IReadOnlyList<string> originals, CancellationToken ct)
    {
        var row = new SandboxCapturedMessage
        {
            TenantCode = DefaultTenant,
            Channel = SandboxChannel.Email,
            IntendedRecipientsJson = JsonSerializer.Serialize(originals),
            Subject = msg.Subject,
            BodyHtml = msg.BodyHtml,
            BodyText = msg.BodyText,
            CapturedAt = clock.UtcNow,
            OriginatingNotificationId = msg.OriginatingNotificationId,
            ProcessInstanceId = msg.ProcessInstanceId,
            TaskId = msg.TaskId,
        };
        db.SandboxCapturedMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<SandboxCapturedMessage> CaptureWebhookAsync(WebhookDelivery msg, CancellationToken ct)
    {
        var row = new SandboxCapturedMessage
        {
            TenantCode = DefaultTenant,
            Channel = SandboxChannel.Webhook,
            IntendedRecipientsJson = JsonSerializer.Serialize(new[] { msg.Url }),
            Url = msg.Url,
            HeadersJson = JsonSerializer.Serialize(msg.Headers),
            PayloadJson = msg.PayloadJson,
            EventType = msg.EventType,
            CapturedAt = clock.UtcNow,
            OriginatingNotificationId = msg.OriginatingNotificationId,
            OriginatingWebhookSubscriptionId = msg.OriginatingWebhookSubscriptionId,
            ProcessInstanceId = msg.ProcessInstanceId,
            TaskId = msg.TaskId,
        };
        db.SandboxCapturedMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<SandboxCapturedMessage> CaptureSmsAsync(SmsMessage msg, CancellationToken ct)
    {
        var row = new SandboxCapturedMessage
        {
            TenantCode = DefaultTenant,
            Channel = SandboxChannel.Sms,
            IntendedRecipientsJson = JsonSerializer.Serialize(msg.To),
            Body = msg.Body,
            CapturedAt = clock.UtcNow,
            OriginatingNotificationId = msg.OriginatingNotificationId,
            ProcessInstanceId = msg.ProcessInstanceId,
            TaskId = msg.TaskId,
        };
        db.SandboxCapturedMessages.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    private async Task<(bool enabled, Bpm.Application.Sandbox.Dtos.SandboxConfigDto? config)> LoadAsync(CancellationToken ct)
    {
        var s = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantCode == DefaultTenant, ct);
        if (s is null) return (false, null);
        Bpm.Application.Sandbox.Dtos.SandboxConfigDto? config = null;
        if (!string.IsNullOrWhiteSpace(s.SandboxConfigJson))
        {
            try { config = JsonSerializer.Deserialize<Bpm.Application.Sandbox.Dtos.SandboxConfigDto>(s.SandboxConfigJson); }
            catch { /* ignore */ }
        }
        return (s.SandboxMode, config);
    }

    private async Task WriteAudit(SandboxChannel channel, SandboxAction action, IEnumerable<string> original, IEnumerable<string> redirected, string sample, CancellationToken ct)
    {
        db.SandboxRedirects.Add(new SandboxRedirect
        {
            TenantCode = DefaultTenant,
            Channel = channel,
            Action = action,
            OriginalTargetsJson = JsonSerializer.Serialize(original),
            RedirectedTargetsJson = JsonSerializer.Serialize(redirected),
            SampleSubject = Truncate(sample, 200),
            DispatchedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s.Substring(0, max);
}

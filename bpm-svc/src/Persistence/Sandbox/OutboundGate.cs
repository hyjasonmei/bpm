using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// Sandbox outbound gate. When sandbox mode is on, the gate <b>captures</b>
/// the full payload into <see cref="SandboxCapturedMessage"/> (returning
/// <see cref="GateOutcome{T}.Capture"/>) so the Mailbox UI can render exactly
/// what the recipient would have seen in production. The PR-J0 "rewrite to alt
/// recipients / drop" path was removed in PR-J6 — capture-only is the contract.
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
        if (!await IsSandboxOnAsync(ct)) return GateOutcome<EmailMessage>.PassThrough(msg);

        var originals = msg.To.Concat(msg.Cc).Concat(msg.Bcc).ToList();
        var captured = await CaptureEmailAsync(msg, originals, ct);
        return GateOutcome<EmailMessage>.Capture(captured.Id);
    }

    public async Task<GateOutcome<WebhookDelivery>> ApplyAsync(WebhookDelivery msg, CancellationToken ct = default)
    {
        if (!await IsSandboxOnAsync(ct)) return GateOutcome<WebhookDelivery>.PassThrough(msg);

        var captured = await CaptureWebhookAsync(msg, ct);
        return GateOutcome<WebhookDelivery>.Capture(captured.Id);
    }

    public async Task<GateOutcome<SmsMessage>> ApplyAsync(SmsMessage msg, CancellationToken ct = default)
    {
        if (!await IsSandboxOnAsync(ct)) return GateOutcome<SmsMessage>.PassThrough(msg);

        var captured = await CaptureSmsAsync(msg, ct);
        return GateOutcome<SmsMessage>.Capture(captured.Id);
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

    private async Task<bool> IsSandboxOnAsync(CancellationToken ct)
    {
        var s = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(x => x.TenantCode == DefaultTenant, ct);
        return s?.SandboxMode ?? false;
    }
}

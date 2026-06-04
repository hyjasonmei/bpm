using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Notifications;
using Bpm.Application.Sandbox;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// <summary>
/// Model-B sandbox sink. When a flow's capture is effective (global
/// <c>SandboxMode</c> OR the flow's own per-flow toggle), records the outbound
/// "email" notification into <c>SandboxCapturedMessages</c> so the admin
/// Sandbox mailbox shows what would have been sent — instead of it silently
/// going nowhere (there is no real SMTP sink yet). The in-app bell sink runs
/// independently in the same composite; capture is additive and never blocks
/// delivery.
/// </summary>
/// <remarks>
/// Per-flow attribution comes from <c>NotifyMessage.Context["flowCode"]</c> /
/// <c>["caseId"]</c> — the same keys the bell uses for deep links (chef
/// SKILL §3 notify example). A message with no <c>flowCode</c>, or whose
/// channels don't include <c>email</c>, is skipped (in-app-only rows are the
/// bell's job, not the mailbox's). Distinct from the retired Model-A
/// <see cref="SandboxCapturingNotificationDispatcher"/> (<c>INotificationDispatcher</c>,
/// <c>SpecSnapshot</c>-driven) which the current chef flows never reach.
/// </remarks>
public sealed class SandboxCaptureNotifyDispatcher(
    AppDbContext db,
    IClock clock,
    IFlowSandboxConfigService flowConfig,
    ILogger<SandboxCaptureNotifyDispatcher> log) : INotifyDispatcher
{
    private const string DefaultTenant = "default";

    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        if (!message.Channels.Contains("email")) return;

        var flowCode = message.Context?.GetValueOrDefault("flowCode");
        if (string.IsNullOrWhiteSpace(flowCode)) return;

        if (!await flowConfig.IsCaptureEffectiveAsync(flowCode, ct)) return;

        Guid? caseId = null;
        if (Guid.TryParse(message.Context?.GetValueOrDefault("caseId"), out var cid)) caseId = cid;

        var recipients = message.Recipients
            .Select(r => r.Email ?? r.DisplayName ?? r.UserId?.ToString() ?? "(unknown)")
            .ToList();

        db.SandboxCapturedMessages.Add(new SandboxCapturedMessage
        {
            Id = Guid.NewGuid(),
            TenantCode = DefaultTenant,
            Channel = SandboxChannel.Email,
            FlowCode = flowCode,
            CaseId = caseId,
            IntendedRecipientsJson = JsonSerializer.Serialize(recipients),
            Subject = message.Subject,
            BodyText = message.Body,
            CapturedAt = clock.UtcNow,
            OriginatingNotificationId = message.SourceId,
            ReadByUserIdsJson = "[]",
        });
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "Sandbox capture → mailbox: {SourceId} flow={Flow} case={Case} recipients={Count}",
            message.SourceId, flowCode, caseId, recipients.Count);
    }
}

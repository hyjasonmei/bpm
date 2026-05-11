using System.Text.Json;
using Bpm.Application.Notifications;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Notifications;

/// <summary>
/// PR-J6 §11 dispatcher: when sandbox is on for the default tenant, walk the
/// snapshot's notifications matching the trigger and feed each one through
/// <see cref="IOutboundGate"/> as a synthesized <see cref="EmailMessage"/> so
/// it lands in <c>SandboxCapturedMessages</c> for the Mailbox UI + the bundle
/// reproducibility runner's assertion pass to find. When sandbox is off,
/// behaves exactly like <see cref="LoggingNotificationDispatcher"/> — logs and
/// returns; no real notification engine yet (that lives in
/// <c>add-notification-engine</c>).
/// </summary>
/// <remarks>
/// <para>
/// Subject + body are pulled from the spec's
/// <c>notifications[].template.subject</c> / <c>.body</c> objects. Locale
/// preference is <c>zh-TW</c> first (the only locale the LEAVE bundle has),
/// falling back to <c>default</c> then to the first key found. The Mustache
/// variable substitution (<c>{{applicant.name}}</c> etc.) is intentionally
/// NOT applied here — captured rows surface the raw template text so
/// reproducibility assertions match deterministically across runs without
/// caring about the org-snapshot's specific user names. Real rendering is
/// the notification engine's job, not the sandbox capture's.
/// </para>
///
/// <para>
/// Recipient resolution is best-effort: we surface the spec's
/// <c>recipients[].type</c> markers verbatim into <c>EmailMessage.To</c>
/// (e.g. <c>"current_approver"</c>, <c>"submitter"</c>) so the Mailbox shows
/// "who would have received this" without the runner needing to walk the
/// org graph. Bundle test cases that want to assert recipients can match on
/// these markers; full org-resolution lands when the real notification
/// engine ships.
/// </para>
/// </remarks>
public sealed class SandboxCapturingNotificationDispatcher(
    IOutboundGate gate,
    AppDbContext db,
    ILogger<SandboxCapturingNotificationDispatcher> logger)
    : INotificationDispatcher
{
    private const string DefaultTenant = "default";

    public async Task DispatchAsync(SpecSnapshot snapshot, string trigger, NotificationContext ctx, CancellationToken ct = default)
    {
        var settings = await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == DefaultTenant, ct);

        if (settings is null || !settings.SandboxMode)
        {
            // Mirror the legacy LoggingNotificationDispatcher contract — log and
            // return so non-sandbox runs stay observable but don't write rows.
            var matched = snapshot.GetNotifications(trigger);
            logger.LogInformation(
                "Notification dispatch (sandbox off): trigger={Trigger} instance={Instance} spec={Spec} matchedSpecs={Count}",
                trigger, ctx.InstanceId, ctx.SpecCode, matched.Count);
            return;
        }

        foreach (var notif in snapshot.GetNotifications(trigger))
        {
            var (subject, bodyHtml, bodyText) = RenderTemplates(notif.Raw);
            var recipients = ResolveRecipientMarkers(notif.Raw);

            var msg = new EmailMessage(
                To: recipients,
                Cc: Array.Empty<string>(),
                Bcc: Array.Empty<string>(),
                Subject: subject,
                BodyHtml: bodyHtml,
                BodyText: bodyText,
                OriginatingNotificationId: notif.Id,
                ProcessInstanceId: ctx.InstanceId);

            await gate.ApplyAsync(msg, ct);
        }
    }

    /// <summary>
    /// Pulls subject/body from the notification's <c>template</c> sub-object.
    /// Spec shape (per <c>spec_schema.md</c> §2.7):
    ///   "template": { "subject": { "zh-TW": "...", "default": "..." },
    ///                 "body":    { "zh-TW": "...", "default": "..." } }
    /// </summary>
    private static (string subject, string bodyHtml, string bodyText) RenderTemplates(JsonElement raw)
    {
        if (!raw.TryGetProperty("template", out var tpl) || tpl.ValueKind != JsonValueKind.Object)
            return ("", "", "");

        var subject = PickLocalized(tpl, "subject");
        var body = PickLocalized(tpl, "body");
        // bodyHtml = bodyText for v1 — the spec template is plain text and the
        // notification engine will own real HTML rendering. Surfacing the same
        // string in both fields keeps the Mailbox's HTML-preview pane non-empty.
        return (subject, body, body);
    }

    private static string PickLocalized(JsonElement template, string fieldName)
    {
        if (!template.TryGetProperty(fieldName, out var f)) return "";
        if (f.ValueKind == JsonValueKind.String) return f.GetString() ?? "";
        if (f.ValueKind != JsonValueKind.Object) return "";

        if (f.TryGetProperty("zh-TW", out var zh) && zh.ValueKind == JsonValueKind.String)
            return zh.GetString() ?? "";
        if (f.TryGetProperty("default", out var d) && d.ValueKind == JsonValueKind.String)
            return d.GetString() ?? "";

        // First string value wins as a final fallback.
        foreach (var p in f.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString() ?? "";
        }
        return "";
    }

    /// <summary>
    /// Surfaces the spec's <c>recipients[].type</c> markers as the email "To"
    /// list. Examples: <c>"current_approver"</c>, <c>"submitter"</c>, <c>"role:HR"</c>.
    /// Bundle assertions can match on these without the runner needing the
    /// full org graph.
    /// </summary>
    private static IReadOnlyList<string> ResolveRecipientMarkers(JsonElement raw)
    {
        if (!raw.TryGetProperty("recipients", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var markers = new List<string>();
        foreach (var r in arr.EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object) continue;
            var type = r.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            if (string.IsNullOrEmpty(type)) continue;

            // Decorate with discriminator-specific suffix when present so
            // assertions can target "role:HR" precisely.
            if (type == "role" && r.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                markers.Add($"role:{code.GetString()}");
            else if (type == "user" && r.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.String)
                markers.Add($"user:{u.GetString()}");
            else if (type == "group" && r.TryGetProperty("groupId", out var g) && g.ValueKind == JsonValueKind.String)
                markers.Add($"group:{g.GetString()}");
            else
                markers.Add(type);
        }
        return markers;
    }
}

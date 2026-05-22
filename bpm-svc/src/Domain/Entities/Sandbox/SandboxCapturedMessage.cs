using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Sandbox;

/// <summary>
/// One row per outbound notification / webhook / SMS captured by the sandbox
/// when sandbox mode is on. Replaces the legacy subject-only
/// <c>SandboxRedirect</c> log (removed in PR-J6) so the Mailbox UI can render
/// the full payload an end-user would have received in production.
/// </summary>
/// <remarks>
/// JSON columns are plain TEXT (no SQLite-specific json_extract) — see the
/// project DB convention notes. Default values keep <c>!= null</c> guarantees
/// for collection columns so callers can deserialize unconditionally.
/// </remarks>
public sealed class SandboxCapturedMessage : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";

    public Guid? ProcessInstanceId { get; set; }
    public Guid? TaskId { get; set; }

    public SandboxChannel Channel { get; set; }

    /// <summary>JSON array of strings: emails / phones / "*" for webhook.</summary>
    public string IntendedRecipientsJson { get; set; } = "[]";

    // Email
    public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public string? BodyText { get; set; }

    // Webhook
    public string? Url { get; set; }
    /// <summary>JSON object of header k/v pairs.</summary>
    public string? HeadersJson { get; set; }
    public string? PayloadJson { get; set; }
    public string? EventType { get; set; }

    // Sms
    public string? Body { get; set; }

    public DateTime CapturedAt { get; set; }

    /// <summary>JSON array of user-id strings (per-persona read tracking).</summary>
    public string ReadByUserIdsJson { get; set; } = "[]";

    /// <summary>spec.json notification id (string per spec convention).</summary>
    public string? OriginatingNotificationId { get; set; }
    public string? OriginatingWebhookSubscriptionId { get; set; }
}

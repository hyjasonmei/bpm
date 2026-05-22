namespace Bpm.Application.Sandbox;

/// <summary>
/// Outbound email payload presented to the sandbox gate. Optional
/// originating-context fields let the gate correlate captures back to the
/// running process for the Mailbox UI; existing call sites that don't yet
/// pass them remain source-compatible (defaults are <c>null</c>).
/// </summary>
public sealed record EmailMessage(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string BodyHtml,
    string BodyText,
    string? OriginatingNotificationId = null,
    Guid? ProcessInstanceId = null,
    Guid? TaskId = null);

public sealed record WebhookDelivery(
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string PayloadJson,
    string EventType,
    string? OriginatingWebhookSubscriptionId = null,
    string? OriginatingNotificationId = null,
    Guid? ProcessInstanceId = null,
    Guid? TaskId = null);

public sealed record SmsMessage(
    IReadOnlyList<string> To,
    string Body,
    string? OriginatingNotificationId = null,
    Guid? ProcessInstanceId = null,
    Guid? TaskId = null);

/// <summary>
/// Result of running a message through the sandbox gate.
/// </summary>
/// <remarks>
/// Four mutually-exclusive shapes:
/// <list type="bullet">
///   <item><c>PassThrough</c>: sandbox off, deliver normally.</item>
///   <item><c>Rewrote</c>: legacy mode, deliver the rewritten <see cref="Message"/> to the alt recipients/url.</item>
///   <item><c>Dropped</c>: legacy mode + no alt configured, do nothing.</item>
///   <item><c>Captured</c>: default sandbox mode — the payload was persisted to <c>SandboxCapturedMessages</c>
///     and the consumer SHOULD treat the delivery as successful WITHOUT invoking the network.
///     For webhooks this is the "fake 200 OK" contract: surface success up the dispatcher chain
///     so retry logic does not engage. <see cref="CapturedMessageId"/> is the row id for correlation.</item>
/// </list>
/// </remarks>
public sealed record GateOutcome<T>(
    T? Message,
    bool Dropped,
    bool Rewritten,
    bool Captured,
    Guid? CapturedMessageId)
{
    public static GateOutcome<T> PassThrough(T message) => new(message, Dropped: false, Rewritten: false, Captured: false, CapturedMessageId: null);
    public static GateOutcome<T> Rewrote(T message) => new(message, Dropped: false, Rewritten: true, Captured: false, CapturedMessageId: null);
    public static GateOutcome<T> DropMessage() => new(default, Dropped: true, Rewritten: false, Captured: false, CapturedMessageId: null);

    /// <summary>
    /// Sandbox captured the full payload to <c>SandboxCapturedMessages</c>.
    /// Consumers (mail relay, webhook dispatcher, SMS gateway) MUST treat
    /// this as success and skip the actual network/SMTP/HTTP call.
    /// Trailing underscore avoids collision with C#'s <c>captured</c> usage in
    /// pattern positions; the descriptive verb is <c>Capture</c>.
    /// </summary>
    public static GateOutcome<T> Capture(Guid capturedId) => new(default, Dropped: false, Rewritten: false, Captured: true, CapturedMessageId: capturedId);
}

public interface IOutboundGate
{
    Task<GateOutcome<EmailMessage>> ApplyAsync(EmailMessage msg, CancellationToken ct = default);
    Task<GateOutcome<WebhookDelivery>> ApplyAsync(WebhookDelivery msg, CancellationToken ct = default);
    Task<GateOutcome<SmsMessage>> ApplyAsync(SmsMessage msg, CancellationToken ct = default);
}

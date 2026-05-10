namespace Bpm.Application.Sandbox;

public sealed record EmailMessage(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string BodyHtml,
    string BodyText);

public sealed record WebhookDelivery(
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string PayloadJson,
    string EventType);

public sealed record SmsMessage(
    IReadOnlyList<string> To,
    string Body);

public sealed record GateOutcome<T>(T? Message, bool Dropped, bool Rewritten)
{
    public static GateOutcome<T> PassThrough(T message) => new(message, Dropped: false, Rewritten: false);
    public static GateOutcome<T> Rewrote(T message) => new(message, Dropped: false, Rewritten: true);
    public static GateOutcome<T> DropMessage() => new(default, Dropped: true, Rewritten: false);
}

public interface IOutboundGate
{
    Task<GateOutcome<EmailMessage>> ApplyAsync(EmailMessage msg, CancellationToken ct = default);
    Task<GateOutcome<WebhookDelivery>> ApplyAsync(WebhookDelivery msg, CancellationToken ct = default);
    Task<GateOutcome<SmsMessage>> ApplyAsync(SmsMessage msg, CancellationToken ct = default);
}

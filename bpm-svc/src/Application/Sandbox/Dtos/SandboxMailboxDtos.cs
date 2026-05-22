using Bpm.Domain.Entities.Sandbox;

namespace Bpm.Application.Sandbox.Dtos;

/// <summary>
/// PR-J4 §7.1: list-row projection of <see cref="SandboxCapturedMessage"/>.
/// Just the columns the Mailbox tab actually renders so the API response
/// stays small under heavy capture load. Full payload (HTML body, headers,
/// JSON) is fetched on demand via the GET-by-id endpoint.
/// </summary>
public sealed record CapturedMessageSummaryDto(
    Guid Id,
    Guid? ProcessInstanceId,
    Guid? TaskId,
    SandboxChannel Channel,
    string? Subject,
    string? EventType,
    DateTime CapturedAt,
    bool ReadByMe);

/// <summary>
/// PR-J4 §7.2: full captured-message detail. All optional payload fields are
/// surfaced so the modal can render whichever shape the channel produced
/// (Email → Subject + BodyHtml + BodyText; Webhook → Url + Headers + Payload
/// + EventType; SMS → Body).
/// </summary>
public sealed record CapturedMessageDetailDto(
    Guid Id,
    Guid? ProcessInstanceId,
    Guid? TaskId,
    SandboxChannel Channel,
    IReadOnlyList<string> IntendedRecipients,
    string? Subject,
    string? BodyHtml,
    string? BodyText,
    string? Url,
    string? HeadersJson,
    string? PayloadJson,
    string? EventType,
    string? Body,
    DateTime CapturedAt,
    bool ReadByMe,
    string? OriginatingNotificationId,
    string? OriginatingWebhookSubscriptionId);

/// <summary>
/// PR-J4 §7.4: counter shape powering the SandboxBanner badge. <c>ByChannel</c>
/// is always populated (zero entries when sandbox is off) so the frontend
/// can render uniformly without null-guarding.
/// </summary>
public sealed record UnreadCountDto(
    int Total,
    IReadOnlyDictionary<string, int> ByChannel);

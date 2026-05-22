using System.Text.Json;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// One bundled test-case. Drives the reproducibility runner (PR-I5) and
/// is also written verbatim into <c>test-cases/{id}.json</c> inside the
/// zip so downstream consumers can replay manually.
///
/// <para>
/// <c>Inputs</c> is kept as a raw <see cref="JsonElement"/> so the wizard's
/// posted form-data shape passes through without normalization — any
/// re-serialization happens once, inside <see cref="BundleBuilder"/>.
/// </para>
///
/// <para>
/// <c>ExpectedNotifications</c> and <c>ExpectedWebhooks</c> (PR-J6 §11.1)
/// are optional assertion templates the reproducibility runner checks
/// against rows in <c>SandboxCapturedMessages</c> after each case
/// completes. Both are nullable so older bundles built before the
/// sandbox-coupling work continue to run unchanged — null means no
/// assertion at all (not "expect zero captures").
/// </para>
/// </summary>
public sealed record TestCaseSnapshot(
    string Id,
    string Name,
    JsonElement Inputs,
    IReadOnlyList<string> ExpectedTrace,
    string ExpectedFinalStatus = "Completed",
    IReadOnlyList<ExpectedNotification>? ExpectedNotifications = null,
    IReadOnlyList<ExpectedWebhook>? ExpectedWebhooks = null);

/// <summary>
/// Optional notification assertion template. v1 matches by:
///   - <c>NotificationId</c> against <c>SandboxCapturedMessage.OriginatingNotificationId</c>
///     when provided (exact equality).
///   - <c>SubjectContains</c> as a case-sensitive substring of the captured Subject.
///   - <c>RecipientUserEmails</c> against the JSON-array stored in
///     <c>IntendedRecipientsJson</c> — string-array containment only (no
///     org-graph resolution yet, see PR-J6 §11.3 [~]).
/// </summary>
public sealed record ExpectedNotification(
    string? NotificationId = null,
    string? SubjectContains = null,
    IReadOnlyList<string>? RecipientUserEmails = null);

/// <summary>
/// Optional webhook assertion template. v1 matches by:
///   - <c>SubscriptionId</c> against <c>OriginatingWebhookSubscriptionId</c>.
///   - <c>EventType</c> against the captured EventType (exact equality).
///   - <c>PayloadSchema</c> reserved for a future structural diff — currently
///     unused by the runner; surfaced on the record so bundles built today
///     don't need a re-encode when the diff lands.
/// </summary>
public sealed record ExpectedWebhook(
    string? SubscriptionId = null,
    string? EventType = null,
    JsonElement? PayloadSchema = null);

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Outcome of replaying every <see cref="TestCaseSnapshot"/> bundled
/// with a spec against a freshly-seeded scratch tenant. <c>Pass</c> iff
/// every individual case passed; first failure flips
/// <see cref="OverallStatus"/> to <c>Fail</c> but the runner still
/// continues to populate per-case diagnostics for the report.
/// </summary>
public sealed record ReproReport(ReproStatus OverallStatus, IReadOnlyList<CaseResult> Cases);

public enum ReproStatus { Pass, Fail }

/// <summary>
/// Per-case outcome. <see cref="ExpectedTrace"/> mirrors
/// <c>TestCaseSnapshot.ExpectedTrace</c>; <see cref="ActualTrace"/> is
/// the ordered list of <c>nodeId</c> values pulled from
/// <c>TaskHistory.TaskSpawned</c> rows. <see cref="Diff"/> is non-null
/// when the two lists differ — formatted as a single string for direct
/// surfacing in the UI / API response.
///
/// <para>
/// PR-J6 §11.4: <see cref="NotificationAssertions"/> /
/// <see cref="WebhookAssertions"/> carry the per-case sandbox-capture
/// assertion outcomes. Both are nullable — null means the test case
/// declared no expectations of that kind, an empty list means it
/// declared expectations but none were emitted (which is itself a
/// failure surfaced as an assertion entry with <c>Passed = false</c>).
/// </para>
/// </summary>
public sealed record CaseResult(
    string CaseId,
    ReproStatus Status,
    IReadOnlyList<string> ExpectedTrace,
    IReadOnlyList<string> ActualTrace,
    string? Diff,
    IReadOnlyList<NotificationAssertion>? NotificationAssertions = null,
    IReadOnlyList<WebhookAssertion>? WebhookAssertions = null);

/// <summary>
/// One ExpectedNotification ↔ captured-message assertion. <see cref="Expected"/>
/// is the human-readable rendering of the test-case template (subject
/// substring + notification id + recipient list); <see cref="Actual"/> is the
/// best matching captured message's identifier (or <c>"(none)"</c> when no
/// candidate row was found). <see cref="Diff"/> is null on pass.
/// </summary>
public sealed record NotificationAssertion(string? Expected, string? Actual, bool Passed, string? Diff);

/// <summary>One ExpectedWebhook ↔ captured-message assertion (mirror of <see cref="NotificationAssertion"/>).</summary>
public sealed record WebhookAssertion(string? Expected, string? Actual, bool Passed, string? Diff);

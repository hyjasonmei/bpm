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
/// </summary>
public sealed record CaseResult(
    string CaseId,
    ReproStatus Status,
    IReadOnlyList<string> ExpectedTrace,
    IReadOnlyList<string> ActualTrace,
    string? Diff);

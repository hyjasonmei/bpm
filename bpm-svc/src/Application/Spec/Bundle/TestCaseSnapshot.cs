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
/// </summary>
public sealed record TestCaseSnapshot(
    string Id,
    string Name,
    JsonElement Inputs,
    IReadOnlyList<string> ExpectedTrace,
    string ExpectedFinalStatus = "Completed");

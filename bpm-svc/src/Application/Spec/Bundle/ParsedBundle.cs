using System.Text.Json;
using Bpm.Domain.Spec.Bundle;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Hydrated, in-memory view of a parsed bundle. Mirrors the canonical
/// on-disk shape (see <see cref="IBundleBuilder"/> output) but with each
/// known file decoded into the type the rest of the system expects.
///
/// <para>
/// <see cref="ManifestChecksum"/> is the SHA-256 of <c>manifest.json</c>
/// itself (uppercase hex) — used downstream as the bundle's stable id
/// (PR-I4 stores it on <c>SpecBundle.ManifestChecksum</c>).
/// </para>
/// </summary>
public sealed record ParsedBundle(
    BundleManifest Manifest,
    string ManifestChecksum,
    JsonElement SpecJson,
    string BpmnXml,
    string SpecMd,
    string? Readme,
    string? Walkthrough,
    string? Changelog,
    IReadOnlyDictionary<string, JsonElement> Forms,
    IReadOnlyDictionary<string, JsonElement> Notifications,
    JsonElement? Sla,
    JsonElement? Actors,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot> TestCases,
    IReadOnlyDictionary<string, byte[]> Assets,
    IReadOnlyList<string> UnknownEntries);

namespace Bpm.Admin.Domain.Bundle;

/// Top-level `manifest.json` shape. Loaders MUST read this first and
/// only trust files enumerated in `Files`; anything else in the zip is
/// ignored for forward-compat (see spec.md "Unlisted file ignored").
public sealed record BundleManifest(
    int BundleSchemaVersion,
    string FlowCode,
    int FlowVersion,
    DateTimeOffset ExportedAt,
    string SourceInstanceId,
    string? Parent,
    IReadOnlyList<BundleFileEntry> Files);

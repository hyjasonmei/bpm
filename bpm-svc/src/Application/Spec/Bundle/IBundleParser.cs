namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Reads a canonical bundle <c>.zip</c> stream into a fully hydrated
/// <see cref="ParsedBundle"/>. Pure: no DB, no filesystem.
///
/// <para>
/// Inverse of <see cref="IBundleBuilder"/>. Reads <c>manifest.json</c>
/// first, verifies SHA-256 for every listed file, then routes bytes by
/// <see cref="Bpm.Domain.Spec.Bundle.BundleFileKind"/>. Unlisted entries
/// are surfaced via <see cref="ParsedBundle.UnknownEntries"/> for
/// forward-compat (per spec.md "Unlisted file ignored").
/// </para>
/// </summary>
public interface IBundleParser
{
    Task<ParsedBundle> ParseAsync(Stream zip, CancellationToken ct = default);
}

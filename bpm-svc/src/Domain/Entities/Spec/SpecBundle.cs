using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Spec;

/// <summary>
/// Persisted Spec Bundle row — the durable record behind the Flow Library
/// (PR-I5/I6). One row per uploaded / built bundle.
///
/// <para>
/// <see cref="ManifestChecksum"/> is the SHA-256 (uppercase hex) of
/// <c>manifest.json</c> bytes — the same value computed by the parser
/// (<c>ParsedBundle.ManifestChecksum</c>). Unique across all tenants so
/// re-importing the byte-identical bundle resolves to the existing row
/// rather than duplicating storage.
/// </para>
///
/// <para>
/// <see cref="ZipBlob"/> is the canonical artefact — every other field
/// (manifest, parsed pieces) can be re-derived from it. Storing it lets
/// the export endpoint stream the original bytes back without rebuilding.
/// </para>
/// </summary>
public sealed class SpecBundle : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owning tenant. Matches the convention used by every other
    /// per-tenant entity in this codebase (see <c>ProcessInstance</c>
    /// etc.) — stored as the tenant code string, not a foreign-key Guid.
    /// </summary>
    public string TenantCode { get; set; } = "default";

    public string FlowCode { get; set; } = string.Empty;
    public int FlowVersion { get; set; }

    /// <summary>SHA-256 (uppercase hex) of the manifest.json bytes.</summary>
    public string ManifestChecksum { get; set; } = string.Empty;

    /// <summary>Manifest checksum of the parent bundle this one was forked from, if any.</summary>
    public string? ParentManifestChecksum { get; set; }

    /// <summary>Verbatim manifest JSON text — useful for cheap metadata reads without unzipping the blob.</summary>
    public string ManifestJson { get; set; } = "{}";

    public byte[] ZipBlob { get; set; } = Array.Empty<byte>();

    public SpecBundleStatus Status { get; set; } = SpecBundleStatus.Draft;

    public DateTime? LastReproCheckAt { get; set; }
    public string? LastReproCheckResultJson { get; set; }
}

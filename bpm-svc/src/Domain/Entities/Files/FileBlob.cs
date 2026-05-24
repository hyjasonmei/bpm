using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Files;

/// <summary>
/// Metadata row for an uploaded file. Bytes live on disk under the file-storage
/// root (see <c>IFileBlobStore</c>) — this table only stores what's needed to
/// look the file up and serve it back. Feature folders reference uploads by
/// <see cref="Id"/> (a Guid string) inside their own form-data JSON.
/// </summary>
/// <remarks>
/// Keep this entity allocation-light and SQLite-safe (no JSON-path queries,
/// no DB-specific types) so the file-storage primitive can move to Postgres
/// with nothing more than a connection-string swap. Bytes-on-disk lets the
/// SQLite file stay small even with many uploads.
/// </remarks>
public sealed class FileBlob : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type the client sent (or sniffed default). Trusted as a hint, not as a security boundary.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>SHA-256 hex of the stored bytes, computed at upload time.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>User-id (admin principal) of the uploader. "system" for boot-time seeded files.</summary>
    public string UploadedBy { get; set; } = "system";

    public DateTime UploadedAt { get; set; }
}

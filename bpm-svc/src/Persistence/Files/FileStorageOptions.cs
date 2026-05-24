namespace Bpm.Persistence.Files;

/// <summary>
/// Bound from configuration section <c>Files</c>. Defaults: 10 MiB cap,
/// blobs land under <c>&lt;repoRoot&gt;/db/files/</c>.
/// </summary>
public sealed class FileStorageOptions
{
    public const long DefaultMaxBytes = 10 * 1024 * 1024;

    /// <summary>Per-file upload size limit. Requests over this fail with 413.</summary>
    public long MaxBytes { get; set; } = DefaultMaxBytes;

    /// <summary>
    /// Directory where blob bytes are stored. Relative paths resolve under
    /// <c>&lt;repoRoot&gt;/db/&lt;value&gt;/</c>; absolute paths pass through.
    /// </summary>
    public string? RootPath { get; set; }
}

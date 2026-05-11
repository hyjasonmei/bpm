namespace Bpm.Domain.Spec.Bundle;

/// One row in `manifest.files[]`. `Sha256` is the lowercase hex digest of
/// the file's raw bytes inside the zip — the parser recomputes it on read
/// to detect tampering. `Path` is the zip-relative POSIX path.
public sealed record BundleFileEntry(
    string Path,
    string Sha256,
    long SizeBytes,
    BundleFileKind Kind);

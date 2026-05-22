namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Thrown by <see cref="BundleParser"/> when the bundle zip is structurally
/// invalid: missing manifest, unknown schema version, file referenced in
/// manifest but absent from the zip, or a SHA-256 mismatch indicating
/// tampering. Cross-file consistency errors flow through
/// <see cref="BundleValidator"/> instead — those are recoverable, parse
/// errors are not.
/// </summary>
public sealed class BundleParseException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public BundleParseException(string message, IReadOnlyList<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? new[] { message };
    }
}

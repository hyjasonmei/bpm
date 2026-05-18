namespace Bpm.Admin.Application.Bundle;

/// <summary>
/// Thrown by <see cref="BundleBuildValidator"/> (and short-circuits
/// <see cref="BundleBuilder.BuildAsync"/>) when bundle inputs are invalid.
///
/// <para>
/// NOTE: The PR brief asked us to extend
/// <c>Bpm.Application.Common.Exceptions.ValidationException</c>, but that
/// class is <c>sealed</c> (and tightly coupled to a FluentValidation
/// failure dictionary). We model this as a standalone exception with a
/// flat <see cref="Errors"/> list — exception-mapping middleware should
/// route it the same way it routes other 400-level validation errors.
/// </para>
/// </summary>
public sealed class BundleBuildException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public BundleBuildException(IReadOnlyList<string> errors)
        : base("Bundle build failed: " + string.Join("; ", errors))
    {
        Errors = errors;
    }
}

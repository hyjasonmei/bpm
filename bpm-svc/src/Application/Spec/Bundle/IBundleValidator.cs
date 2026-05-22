namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Cross-file consistency checks for a parsed bundle. Distinct from
/// <see cref="BundleParseException"/>: those are structural errors (bad
/// zip / bad manifest / SHA mismatch), this is "the files individually
/// parsed but they don't agree with each other".
///
/// <para>
/// Pure: takes a hydrated <see cref="ParsedBundle"/>, returns a result.
/// No DB, no logging side-effects. Caller decides whether to bin the
/// bundle, surface errors to the wizard UI, or roll forward in
/// "InstalledUnverified" status (PR-I4).
/// </para>
/// </summary>
public interface IBundleValidator
{
    BundleValidationResult Validate(ParsedBundle b);
}

public sealed record BundleValidationResult(bool Valid, IReadOnlyList<BundleValidationError> Errors)
{
    public static BundleValidationResult Ok() => new(true, Array.Empty<BundleValidationError>());
    public static BundleValidationResult Fail(params BundleValidationError[] errs) => new(false, errs);
}

public sealed record BundleValidationError(string Code, string Location, string Message);

namespace Bpm.Domain.Spec.Bundle;

/// Single source of truth for the on-disk bundle schema version. Builders
/// stamp `Current` into every manifest they emit; parsers reject any value
/// greater than `Current` to keep forward-compat boundaries explicit.
public static class BundleSchemaVersion
{
    public const int Current = 1;
}

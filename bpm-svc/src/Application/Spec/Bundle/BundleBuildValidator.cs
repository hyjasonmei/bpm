namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Pre-build validation for <see cref="BundleBuildRequest"/>. Throws a
/// <see cref="BundleBuildException"/> with the full error list — never
/// returns half-validated state. Per-asset / total-bundle size limits
/// are deferred until the assets pipeline lands (see tasks 2.7 + 7.x).
/// </summary>
public sealed class BundleBuildValidator
{
    public void Validate(BundleBuildRequest req)
    {
        var errors = new List<string>();

        if (req.SampleOrg is null || req.SampleOrg.Users.Count == 0)
        {
            errors.Add("sample-org must contain >=1 user");
        }

        if (req.TestCases is null || req.TestCases.Count == 0)
        {
            errors.Add("at least one test-case required");
        }

        if (errors.Count > 0)
        {
            throw new BundleBuildException(errors);
        }
    }
}

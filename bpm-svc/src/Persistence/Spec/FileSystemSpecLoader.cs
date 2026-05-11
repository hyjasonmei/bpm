using Bpm.Application.Spec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Spec;

/// <summary>
/// V1 spec loader: reads spec text from the on-disk <c>sample_specs/</c>
/// directory configured by <c>Bpm:SampleSpecsDir</c>. Falls back to a path
/// derived from <see cref="AppContext.BaseDirectory"/> so it works from both
/// the bpm-svc app and the test runner without env tweaks.
/// </summary>
// TODO: replace with a ProcessVersion-table lookup once spec versioning lands.
public sealed class FileSystemSpecLoader(IConfiguration configuration, ILogger<FileSystemSpecLoader> logger) : ISpecLoader
{
    private readonly string _sampleDir = ResolveSampleDir(configuration);

    public async Task<string?> LoadAsync(string tenantCode, string specCode, CancellationToken ct = default)
    {
        // Convention: <SampleDir>/<spec_code_lowered>_v1.json
        var fileName = $"{specCode.ToLowerInvariant()}_v1.json";
        var path = Path.Combine(_sampleDir, fileName);
        if (File.Exists(path))
        {
            logger.LogInformation("SpecLoader: loaded {SpecCode} from {Path}", specCode, path);
            return await File.ReadAllTextAsync(path, ct);
        }

        // Secondary fallback: incoming queue per tenant (latest by name).
        var incomingDir = Path.GetFullPath(Path.Combine(_sampleDir, "..", "specs-incoming", tenantCode));
        if (Directory.Exists(incomingDir))
        {
            var match = Directory.EnumerateFiles(incomingDir, $"*-{specCode}.json")
                                 .OrderByDescending(p => p)
                                 .FirstOrDefault();
            if (match is not null)
            {
                logger.LogInformation("SpecLoader: loaded {SpecCode} from incoming {Path}", specCode, match);
                return await File.ReadAllTextAsync(match, ct);
            }
        }

        logger.LogWarning("SpecLoader: no spec found for {SpecCode} (tenant={Tenant}) in {Dir}",
            specCode, tenantCode, _sampleDir);
        return null;
    }

    private static string ResolveSampleDir(IConfiguration cfg)
    {
        var configured = cfg["Bpm:SampleSpecsDir"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        // Default: walk up from the executing assembly to the repo root and
        // hit `sample_specs/`. From bin/Debug/net10.0 that's six levels up.
        var fallback = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "sample_specs"));
        return fallback;
    }
}

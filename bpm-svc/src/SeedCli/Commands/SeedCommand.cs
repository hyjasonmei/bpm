using Bpm.Persistence;
using Bpm.SeedCli.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bpm.SeedCli.Commands;

/// <summary>
/// Optionally builds + installs every <c>sample_specs/*.json</c> as a
/// SpecBundle row. Identity seeding is owned by admin-svc post
/// unify-user-store; bpm-svc no longer maintains a persona seed.
///
/// <para>
/// Idempotent on <c>SpecBundle.ManifestChecksum</c> — re-running on a
/// populated DB re-applies nothing.
/// </para>
/// </summary>
public static class SeedCommand
{
    public static async Task<int> RunAsync(
        bool includeBundles,
        string sampleSpecsDir,
        IServiceProvider services,
        AppDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("[OK] migrations up to date");

            if (includeBundles)
            {
                var fixture = new BundleSeedFixture(
                    db,
                    services.GetRequiredService<Services.BundleInstaller>(),
                    services.GetRequiredService<ILogger<BundleSeedFixture>>());
                var result = await fixture.RunAsync(sampleSpecsDir, ct);
                if (result.Failed > 0)
                {
                    logger.LogError("[FAIL] {Failed} bundle(s) failed to install", result.Failed);
                    foreach (var err in result.Errors)
                        logger.LogError("[FAIL]   {Error}", err);
                    return 1;
                }
                logger.LogInformation("[OK] bundle seed complete: {Inserted} inserted, {Existed} already existed",
                    result.Inserted, result.AlreadyExisted);
            }

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FAIL] seed failed: {Message}", ex.Message);
            return 1;
        }
    }
}

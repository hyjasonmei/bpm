using Bpm.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.SeedCli.Commands;

/// <summary>
/// Drops the SQLite file (if any) and re-applies all EF migrations to a
/// fresh DB. No seed data is written — chain with <c>seed</c> to populate.
/// </summary>
public static class ResetCommand
{
    public static async Task<int> RunAsync(
        string? sqliteFilePath,
        AppDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            // Drop EF connection pool first so File.Delete doesn't trip on a
            // lingering handle from migrations bootstrap.
            SqliteConnection.ClearAllPools();
            await db.Database.CloseConnectionAsync();

            if (!string.IsNullOrWhiteSpace(sqliteFilePath) && File.Exists(sqliteFilePath))
            {
                File.Delete(sqliteFilePath);
                logger.LogInformation("[OK] deleted {DbPath}", sqliteFilePath);

                // SQLite WAL/SHM siblings would also keep stale data around.
                var wal = sqliteFilePath + "-wal";
                var shm = sqliteFilePath + "-shm";
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
            }
            else if (!string.IsNullOrWhiteSpace(sqliteFilePath))
            {
                logger.LogInformation("[INFO] no DB file at {DbPath} (already fresh)", sqliteFilePath);
            }
            else
            {
                logger.LogWarning("[WARN] no SQLite file path resolved from connection string; skipping file delete");
            }

            await db.Database.MigrateAsync(ct);
            logger.LogInformation("[OK] applied EF migrations");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FAIL] reset failed: {Message}", ex.Message);
            return 1;
        }
    }
}

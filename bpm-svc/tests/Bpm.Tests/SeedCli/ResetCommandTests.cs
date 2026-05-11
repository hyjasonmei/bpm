using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.SeedCli.Commands;
using Bpm.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.SeedCli;

/// <summary>
/// PR-L4 §6 — ResetCommand drops the SQLite file (when present) and
/// re-applies migrations. Verified against a real on-disk SQLite file
/// in a temp folder so the file-delete path is exercised.
/// </summary>
public sealed class ResetCommandTests : IDisposable
{
    private readonly string _tempDir;

    public ResetCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "bpm-seedcli-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* tests should not fail on cleanup */ }
    }

    [Fact]
    public async Task Reset_deletes_existing_file_and_reapplies_migrations()
    {
        var dbPath = Path.Combine(_tempDir, "reset_existing.db");
        var connStr = $"Data Source={dbPath}";

        // First boot: write some data.
        await using (var db = NewDb(connStr))
        {
            await db.Database.MigrateAsync();
            db.Roles.Add(new Bpm.Domain.Entities.Authz.Role { Id = Guid.NewGuid(), Code = "marker", Name = "Marker" });
            await db.SaveChangesAsync();
        }
        Assert.True(File.Exists(dbPath));

        await using (var db = NewDb(connStr))
        {
            var rc = await ResetCommand.RunAsync(dbPath, db, NullLogger.Instance);
            Assert.Equal(0, rc);
        }

        // After reset: file exists again (migrations re-applied) but no marker row.
        Assert.True(File.Exists(dbPath));
        await using (var db = NewDb(connStr))
        {
            var hasMarker = await db.Roles.AnyAsync(r => r.Code == "marker");
            Assert.False(hasMarker);
            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.NotEmpty(migrations);
        }
    }

    [Fact]
    public async Task Reset_succeeds_when_no_file_exists_yet()
    {
        var dbPath = Path.Combine(_tempDir, "reset_fresh.db");
        var connStr = $"Data Source={dbPath}";
        Assert.False(File.Exists(dbPath));

        await using var db = NewDb(connStr);
        var rc = await ResetCommand.RunAsync(dbPath, db, NullLogger.Instance);
        Assert.Equal(0, rc);
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public void TryExtractSqliteFile_parses_data_source_key()
    {
        Assert.Equal(Path.GetFullPath("bpm.db"), global::Bpm.SeedCli.Program.TryExtractSqliteFile("Data Source=bpm.db"));
        Assert.Equal("/abs/path/x.db", global::Bpm.SeedCli.Program.TryExtractSqliteFile("DataSource=/abs/path/x.db"));
        Assert.Null(global::Bpm.SeedCli.Program.TryExtractSqliteFile("Data Source=:memory:"));
        Assert.Null(global::Bpm.SeedCli.Program.TryExtractSqliteFile(""));
    }

    private static AppDbContext NewDb(string connStr)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connStr)
            .AddInterceptors(new AuditSaveChangesInterceptor(new StubClock(), new StubCurrentUser()))
            .Options;
        return new AppDbContext(options);
    }
}

using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Seed;
using Bpm.SeedCli.Fixtures;
using Bpm.SeedCli.Services;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.SeedCli;

/// <summary>
/// PR-L4 §5 — BundleSeedFixture turns every <c>sample_specs/*.json</c> into
/// a SpecBundle row in <c>Pending</c> status. Idempotent on
/// <c>ManifestChecksum</c>.
/// </summary>
public sealed class BundleSeedFixtureTests : IDisposable
{
    private const string SampleSpecsDir = "/Users/jason/claude/bpm/sample_specs";

    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public BundleSeedFixtureTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();

        var interceptor = new AuditSaveChangesInterceptor(_clock, new StubCurrentUser());
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_conn)
            .AddInterceptors(interceptor)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _conn.Dispose();

    [Fact]
    public async Task Run_installs_all_sample_specs_as_pending_bundles()
    {
        await SeedPersonaAsync();

        var expected = Directory.GetFiles(SampleSpecsDir, "*.json").Length;
        Assert.True(expected > 0, "no sample_specs/*.json found");

        await using var db = new AppDbContext(_options);
        var fixture = new BundleSeedFixture(db, NewInstaller(db), NullLogger<BundleSeedFixture>.Instance);
        var result = await fixture.RunAsync(SampleSpecsDir);

        Assert.Equal(expected, result.Inserted);
        Assert.Equal(0, result.AlreadyExisted);
        Assert.Equal(0, result.Failed);

        await using var read = new AppDbContext(_options);
        Assert.Equal(expected, await read.SpecBundles.CountAsync());
        Assert.Equal(expected, await read.SpecBundles.CountAsync(b => b.Status == SpecBundleStatus.Pending));
    }

    [Fact]
    public async Task Rerun_does_not_create_duplicate_rows()
    {
        await SeedPersonaAsync();

        // First run — installs all bundles.
        int firstInserted;
        await using (var db = new AppDbContext(_options))
        {
            var fixture = new BundleSeedFixture(db, NewInstaller(db), NullLogger<BundleSeedFixture>.Instance);
            var first = await fixture.RunAsync(SampleSpecsDir);
            firstInserted = first.Inserted;
            Assert.True(firstInserted > 0);
        }

        int rowsAfterFirst;
        await using (var db = new AppDbContext(_options))
        {
            rowsAfterFirst = await db.SpecBundles.CountAsync();
        }

        // Second run — every spec hits the ManifestChecksum guard.
        await using (var db = new AppDbContext(_options))
        {
            var fixture = new BundleSeedFixture(db, NewInstaller(db), NullLogger<BundleSeedFixture>.Instance);
            var second = await fixture.RunAsync(SampleSpecsDir);
            Assert.Equal(0, second.Inserted);
            Assert.Equal(firstInserted, second.AlreadyExisted);
            Assert.Equal(0, second.Failed);
        }

        await using (var db = new AppDbContext(_options))
        {
            Assert.Equal(rowsAfterFirst, await db.SpecBundles.CountAsync());
        }
    }

    [Fact]
    public async Task Run_persists_manifest_checksum_and_zip_blob_per_row()
    {
        await SeedPersonaAsync();

        await using (var db = new AppDbContext(_options))
        {
            var fixture = new BundleSeedFixture(db, NewInstaller(db), NullLogger<BundleSeedFixture>.Instance);
            await fixture.RunAsync(SampleSpecsDir);
        }

        await using var read = new AppDbContext(_options);
        var bundles = await read.SpecBundles.AsNoTracking().ToListAsync();
        Assert.NotEmpty(bundles);
        foreach (var b in bundles)
        {
            Assert.False(string.IsNullOrEmpty(b.FlowCode));
            Assert.True(b.FlowVersion >= 1);
            Assert.False(string.IsNullOrEmpty(b.ManifestChecksum));
            Assert.Equal(64, b.ManifestChecksum.Length); // SHA-256 hex
            Assert.NotEmpty(b.ZipBlob);
            Assert.False(string.IsNullOrEmpty(b.ManifestJson));
        }
    }

    [Fact]
    public async Task Run_throws_when_no_users_seeded()
    {
        await using var db = new AppDbContext(_options);
        var fixture = new BundleSeedFixture(db, NewInstaller(db), NullLogger<BundleSeedFixture>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync(SampleSpecsDir));
    }

    private async Task SeedPersonaAsync()
    {
        await using var db = new AppDbContext(_options);
        await PersonaSeedService.RunAsync(db, NullLogger.Instance);
    }

    private BundleInstaller NewInstaller(AppDbContext db)
    {
        var validator = new BundleBuildValidator();
        var specRenderer = new SpecMdRenderer();
        var walkthroughRenderer = new WalkthroughRenderer();
        var changelogRenderer = new ChangelogRenderer();
        var builder = new BundleBuilder(validator, specRenderer, walkthroughRenderer, changelogRenderer, _clock);
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        return new BundleInstaller(db, builder, parser);
    }
}

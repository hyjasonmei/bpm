using Bpm.Application.Spec.Bundle;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Spec.Bundle;
using Bpm.Tests.Application.Spec.Bundle;
using Bpm.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Persistence.Spec.Bundle;

/// <summary>
/// PR-I4 §5.1-5.4 — runtime loader behaviour:
/// seeds the bundle's sample-org under a derived scratch tenant, reuses
/// the seed on a second load (idempotent), and tears down every row at
/// dispose. The defensive guard against cleanups targeting a real
/// tenant is exercised as a pure unit test on
/// <see cref="BundleRuntimeLoader.AssertScratchTenant"/>.
/// </summary>
public sealed class BundleRuntimeLoaderTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();

    public BundleRuntimeLoaderTests()
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

    // 5.x — happy path: load LEAVE bundle → handle reports the seed, scratch
    // tenant code is derived from flow code + checksum prefix.
    [Fact]
    public async Task Load_seeds_sample_org_under_scratch_tenant()
    {
        await using var db = new AppDbContext(_options);
        var loader = new BundleRuntimeLoader(db, NullLogger<BundleRuntimeLoader>.Instance);
        var parsed = await BuildAndParseAsync();

        await using var handle = await loader.LoadAsync(parsed);

        Assert.True(handle.SeededUserCount > 0);
        Assert.StartsWith("repro-leave-", handle.ScratchTenantCode);
        Assert.Equal("LEAVE", handle.RegisteredSpecCode);
        Assert.False(string.IsNullOrEmpty(handle.SpecJson));
        Assert.NotEqual(Guid.Empty, handle.InitiatorUserId);

        // The seed materialised real rows we can find by namespaced email.
        await using var read = new AppDbContext(_options);
        var users = await read.Users
            .Where(u => u.Email.StartsWith($"{handle.ScratchTenantCode}__"))
            .CountAsync();
        Assert.Equal(parsed.SampleOrg.Users.Count, users);
    }

    // 5.4 dispose — handle's OnDispose deletes every row inserted, leaving
    // no scratch trace behind.
    [Fact]
    public async Task Dispose_removes_all_scratch_rows()
    {
        await using var db = new AppDbContext(_options);
        var loader = new BundleRuntimeLoader(db, NullLogger<BundleRuntimeLoader>.Instance);
        var parsed = await BuildAndParseAsync();

        var handle = await loader.LoadAsync(parsed);
        var scratch = handle.ScratchTenantCode;
        await handle.DisposeAsync();

        await using var read = new AppDbContext(_options);
        var leftoverUsers = await read.Users.Where(u => u.Email.StartsWith($"{scratch}__")).CountAsync();
        Assert.Equal(0, leftoverUsers);

        var leftoverGroups = await read.Groups.Where(g => g.Code.StartsWith($"{scratch}__")).CountAsync();
        Assert.Equal(0, leftoverGroups);
    }

    // 5.4 defensive — cleanup must refuse to operate on tenant codes that
    // don't start with the scratch prefix. Running this as a pure unit test
    // on the helper means we can't accidentally trash production rows even
    // if a future caller hand-rolls a handle pointing at "default".
    [Fact]
    public void Cleanup_refuses_real_tenant()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BundleRuntimeLoader.AssertScratchTenant("default"));
        Assert.Throws<InvalidOperationException>(() =>
            BundleRuntimeLoader.AssertScratchTenant(""));
        // Sanity: a real scratch code passes.
        BundleRuntimeLoader.AssertScratchTenant("repro-leave-abcd1234");
    }

    private static async Task<ParsedBundle> BuildAndParseAsync()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        using var ms = new MemoryStream(bytes);
        return await parser.ParseAsync(ms);
    }
}

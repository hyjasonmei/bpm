using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Bpm.Persistence.Interceptors;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Admin.ProcessAdmin;

/// <summary>
/// PR-K1 §2.6 — Process Admin definitions surface. Each test uses an
/// in-memory SQLite (so SpecBundles starts empty) plus a per-test temp
/// directory injected as <c>Bpm:SampleSpecsDir</c>, so filesystem
/// fixtures can be controlled without disturbing the real
/// <c>sample_specs/</c> tree.
/// </summary>
public sealed class ProcessAdminControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private readonly string _tmpSpecsDir;
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public ProcessAdminControllerTests()
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

        _tmpSpecsDir = Path.Combine(Path.GetTempPath(),
            $"bpm-process-admin-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpSpecsDir);
    }

    public void Dispose()
    {
        _conn.Dispose();
        try { Directory.Delete(_tmpSpecsDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ListDefinitions_returns_bundles_and_filesystem_combined()
    {
        WriteFilesystemSpec("PURCHASE", 1);
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.Installed);

        var result = await BuildController().ListDefinitions(tenantCode: null, default);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.FlowCode == "LEAVE" && d.Source == "bundle");
        Assert.Contains(result, d => d.FlowCode == "PURCHASE" && d.Source == "filesystem");
    }

    [Fact]
    public async Task ListDefinitions_dedups_filesystem_when_bundle_exists_for_same_flowCode()
    {
        // Same flowCode in both stores — bundle wins.
        WriteFilesystemSpec("LEAVE", 1);
        await SeedBundleAsync("LEAVE", 2, SpecBundleStatus.Installed);

        var result = await BuildController().ListDefinitions(tenantCode: null, default);

        var only = Assert.Single(result);
        Assert.Equal("LEAVE", only.FlowCode);
        Assert.Equal("bundle", only.Source);
        Assert.Equal(2, only.Version);
    }

    [Fact]
    public async Task ListDefinitions_excludes_soft_deleted_bundles()
    {
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.SoftDeleted);

        var result = await BuildController().ListDefinitions(tenantCode: null, default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListDefinitions_picks_latest_version_when_multiple_bundles_for_one_flowCode()
    {
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.Installed);
        await SeedBundleAsync("LEAVE", 2, SpecBundleStatus.Installed);
        await SeedBundleAsync("LEAVE", 3, SpecBundleStatus.Pending);

        var result = await BuildController().ListDefinitions(tenantCode: null, default);

        var only = Assert.Single(result);
        Assert.Equal(3, only.Version);
    }

    [Fact]
    public async Task ListVersions_returns_all_versions_ordered_desc()
    {
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.Installed);
        await SeedBundleAsync("LEAVE", 2, SpecBundleStatus.Installed);
        await SeedBundleAsync("LEAVE", 3, SpecBundleStatus.Pending);

        var result = await BuildController().ListVersions("LEAVE", tenantCode: null, default);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result[0].FlowVersion);
        Assert.Equal(2, result[1].FlowVersion);
        Assert.Equal(1, result[2].FlowVersion);
    }

    [Fact]
    public async Task ListVersions_returns_empty_for_unknown_flowCode()
    {
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.Installed);
        var result = await BuildController().ListVersions("UNKNOWN", tenantCode: null, default);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListVersions_excludes_soft_deleted()
    {
        await SeedBundleAsync("LEAVE", 1, SpecBundleStatus.Installed);
        await SeedBundleAsync("LEAVE", 2, SpecBundleStatus.SoftDeleted);

        var result = await BuildController().ListVersions("LEAVE", tenantCode: null, default);
        var only = Assert.Single(result);
        Assert.Equal(1, only.FlowVersion);
    }

    // ===== harness =====

    private ProcessAdminController BuildController()
    {
        var db = new AppDbContext(_options);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bpm:SampleSpecsDir"] = _tmpSpecsDir,
            })
            .Build();
        var controller = new ProcessAdminController(
            db, config, NullLogger<ProcessAdminController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(AdminId) };
        return controller;
    }

    private static DefaultHttpContext HttpContextFor(Guid userId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("roles", "admin"),
        }, authenticationType: "test", nameType: "sub", roleType: "roles");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private async Task SeedBundleAsync(string flowCode, int version, SpecBundleStatus status)
    {
        await using var db = new AppDbContext(_options);
        var manifest = new
        {
            bundleSchemaVersion = 1,
            flowCode,
            flowVersion = version,
            exportedAt = DateTime.UtcNow.AddMinutes(-version).ToString("o"),
            sourceInstanceId = "default",
            parent = (string?)null,
            files = Array.Empty<object>(),
        };
        var manifestJson = JsonSerializer.Serialize(manifest);
        // Keep ManifestChecksum unique per row — it's the unique index key.
        var checksum = $"SHA-{flowCode}-V{version}-{Guid.NewGuid():N}";
        db.SpecBundles.Add(new SpecBundle
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            FlowCode = flowCode,
            FlowVersion = version,
            ManifestChecksum = checksum,
            ManifestJson = manifestJson,
            ZipBlob = Array.Empty<byte>(),
            Status = status,
        });
        await db.SaveChangesAsync();
    }

    private void WriteFilesystemSpec(string flowCode, int version)
    {
        var fileName = $"{flowCode.ToLowerInvariant()}_v{version}.json";
        var path = Path.Combine(_tmpSpecsDir, fileName);
        var spec = new
        {
            meta = new
            {
                schemaVersion = "1.0",
                tenant = "acme",
                flowName = flowCode,
                flowCode,
                flowVersion = version,
            },
            flow = new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(spec));
    }
}

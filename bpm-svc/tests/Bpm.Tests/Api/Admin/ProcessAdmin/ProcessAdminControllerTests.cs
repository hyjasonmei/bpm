using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Bpm.Api.Admin.ProcessAdmin;
using Bpm.Application.Process.Simulator;
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

    /* ── PR-K2 §3.6 — GetSpec ── */

    [Fact]
    public async Task GetSpec_returns_filesystem_spec_when_no_bundle()
    {
        WriteFilesystemSpec("PURCHASE", 1);

        var action = await BuildController().GetSpec("PURCHASE", tenantCode: null, default);
        var content = Assert.IsType<ContentResult>(action);
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"flowCode\":\"PURCHASE\"", content.Content);
    }

    [Fact]
    public async Task GetSpec_prefers_bundle_over_filesystem()
    {
        // Write a fs spec with v=1 and a bundle with v=2 carrying a marker
        // payload; the controller must return the bundle's spec.
        WriteFilesystemSpec("LEAVE", 1);
        const string bundleSpec = "{\"meta\":{\"flowCode\":\"LEAVE\",\"flowVersion\":2,\"marker\":\"from-bundle\"}}";
        await SeedBundleWithSpecAsync("LEAVE", 2, SpecBundleStatus.Installed, bundleSpec);

        var action = await BuildController().GetSpec("LEAVE", tenantCode: null, default);
        var content = Assert.IsType<ContentResult>(action);
        Assert.Contains("from-bundle", content.Content);
    }

    [Fact]
    public async Task GetSpec_picks_latest_bundle_version()
    {
        await SeedBundleWithSpecAsync("LEAVE", 1, SpecBundleStatus.Installed, "{\"meta\":{\"flowVersion\":1,\"tag\":\"v1\"}}");
        await SeedBundleWithSpecAsync("LEAVE", 2, SpecBundleStatus.Installed, "{\"meta\":{\"flowVersion\":2,\"tag\":\"v2\"}}");
        await SeedBundleWithSpecAsync("LEAVE", 3, SpecBundleStatus.Pending, "{\"meta\":{\"flowVersion\":3,\"tag\":\"v3\"}}");

        var action = await BuildController().GetSpec("LEAVE", tenantCode: null, default);
        var content = Assert.IsType<ContentResult>(action);
        Assert.Contains("\"tag\":\"v3\"", content.Content);
    }

    [Fact]
    public async Task GetSpec_excludes_soft_deleted_bundles_falling_through_to_filesystem()
    {
        WriteFilesystemSpec("LEAVE", 1);
        await SeedBundleWithSpecAsync("LEAVE", 5, SpecBundleStatus.SoftDeleted, "{\"meta\":{\"tag\":\"deleted\"}}");

        var action = await BuildController().GetSpec("LEAVE", tenantCode: null, default);
        var content = Assert.IsType<ContentResult>(action);
        Assert.Contains("\"flowCode\":\"LEAVE\"", content.Content);
        Assert.DoesNotContain("deleted", content.Content);
    }

    [Fact]
    public async Task GetSpec_returns_404_when_unknown()
    {
        var action = await BuildController().GetSpec("DOES_NOT_EXIST", tenantCode: null, default);
        Assert.IsType<NotFoundObjectResult>(action);
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
            db, config, new ThrowingSimulator(), NullLogger<ProcessAdminController>.Instance);
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

    /// <summary>
    /// Seed a bundle row whose ZipBlob contains a real <c>spec.json</c>
    /// entry. We construct the zip inline (a single text file) instead of
    /// invoking the full BundleBuilder — the controller only reads
    /// spec.json so a hand-rolled archive is the cheapest valid fixture.
    /// </summary>
    private async Task SeedBundleWithSpecAsync(string flowCode, int version, SpecBundleStatus status, string specJson)
    {
        await using var db = new AppDbContext(_options);
        byte[] zip;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("spec.json");
                using var es = entry.Open();
                es.Write(Encoding.UTF8.GetBytes(specJson));
            }
            zip = ms.ToArray();
        }
        var manifestJson = JsonSerializer.Serialize(new
        {
            bundleSchemaVersion = 1,
            flowCode,
            flowVersion = version,
            exportedAt = DateTime.UtcNow.AddMinutes(-version).ToString("o"),
            sourceInstanceId = "default",
            files = Array.Empty<object>(),
        });
        db.SpecBundles.Add(new SpecBundle
        {
            Id = Guid.NewGuid(),
            TenantCode = "default",
            FlowCode = flowCode,
            FlowVersion = version,
            ManifestChecksum = $"SHA-{flowCode}-V{version}-{Guid.NewGuid():N}",
            ManifestJson = manifestJson,
            ZipBlob = zip,
            Status = status,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The existing controller tests don't exercise <c>POST /simulate</c>
    /// (those are covered by <c>SimulateEndpointTests</c>). Inject a stub
    /// that throws if called so a regression accidentally routing through
    /// the simulator is loud, not silently no-op.
    /// </summary>
    private sealed class ThrowingSimulator : IProcessSimulator
    {
        public Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
            => throw new InvalidOperationException("ThrowingSimulator: not configured for this test");
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

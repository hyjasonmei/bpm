using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using Bpm.Api.Admin.FlowLibrary;
using Bpm.Application.Delegation;
using Bpm.Application.Notifications;
using Bpm.Application.Org;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Bundle;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Bpm.Persistence.Delegation;
using Bpm.Persistence.Interceptors;
using Bpm.Persistence.Org;
using Bpm.Persistence.Process;
using Bpm.Persistence.Spec.Bundle;
using Bpm.Tests.Application.Spec.Bundle;
using Bpm.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Api.Admin.FlowLibrary;

/// <summary>
/// PR-I5 §6 — Flow Library REST endpoints. We construct the controller
/// directly (mirrors <c>ProcessControllerTests</c>) so we can exercise
/// routing decisions, the auth claim flow, and the entire bundle pipeline
/// (parse + validate + persist + repro) against an in-memory SQLite DB.
///
/// Each test class instance owns its own SQLite connection so the
/// SpecBundles table starts empty per test — required because the unique
/// ManifestChecksum index would otherwise make the idempotency test see
/// state from prior cases.
/// </summary>
public sealed class FlowLibraryControllerTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly StubClock _clock = new();
    private static readonly Guid AdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public FlowLibraryControllerTests()
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

    // ===== 6.9 Round-trip — POST import → GET list → GET export =====

    [Fact]
    public async Task Roundtrip_import_then_list_then_export_returns_identical_bytes()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();

        var controller = BuildController();
        var importResult = await controller.Import("install", tenantCode: null, BuildFormFile(bytes), default);

        var created = Assert.IsType<CreatedAtActionResult>(importResult);
        var install = Assert.IsType<ImportInstallResult>(created.Value);
        Assert.Equal(SpecBundleStatus.Installed, install.Status);
        Assert.NotNull(install.ReproReport);
        Assert.Equal(ReproStatus.Pass, install.ReproReport!.OverallStatus);

        // Fresh controller on a new context — production hits a new request.
        var listController = BuildController();
        var list = await listController.List(tenantCode: null, default);
        var item = Assert.Single(list);
        Assert.Equal(install.Id, item.Id);
        Assert.Equal("LEAVE", item.FlowCode);
        Assert.Equal(SpecBundleStatus.Installed, item.Status);
        Assert.NotNull(item.LastReproCheckSummary);
        Assert.StartsWith("PASS", item.LastReproCheckSummary);

        var exportController = BuildController();
        var exportResult = await exportController.Export(install.Id, default);
        var file = Assert.IsType<FileContentResult>(exportResult);
        Assert.Equal("application/zip", file.ContentType);
        Assert.EndsWith(".zip", file.FileDownloadName);
        Assert.Equal(bytes, file.FileContents);
    }

    // ===== List excludes SoftDeleted =====

    [Fact]
    public async Task List_excludes_soft_deleted_rows()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var controller = BuildController();
        var importResult = await controller.Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var deleteController = BuildController();
        var deleteResult = await deleteController.Delete(install.Id, default);
        Assert.IsType<NoContentResult>(deleteResult);

        var listController = BuildController();
        var list = await listController.List(null, default);
        Assert.Empty(list);
    }

    // ===== Get by id 404 paths =====

    [Fact]
    public async Task GetById_returns_404_when_not_found()
    {
        var controller = BuildController();
        var result = await controller.GetById(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_returns_404_when_soft_deleted()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        await BuildController().Delete(install.Id, default);

        var result = await BuildController().GetById(install.Id, default);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetById_returns_detail_with_manifest_and_repro_report()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().GetById(install.Id, default);
        var detail = Assert.IsType<FlowLibraryDetailDto>(result.Value);
        Assert.Equal("LEAVE", detail.Manifest.FlowCode);
        Assert.NotNull(detail.LastReproReport);
        Assert.Equal(ReproStatus.Pass, detail.LastReproReport!.OverallStatus);
    }

    // ===== Files endpoint =====

    [Fact]
    public async Task GetFile_returns_spec_json_with_correct_content_type()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().GetFile(install.Id, "spec.json", default);
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/json", file.ContentType);
        // Assert the bytes are a valid JSON object (not the raw zip bytes).
        using var doc = JsonDocument.Parse(file.FileContents);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public async Task GetFile_rejects_path_traversal_with_400()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().GetFile(install.Id, "../etc/passwd", default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetFile_returns_404_for_unlisted_path()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().GetFile(install.Id, "not-in-manifest.json", default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ===== Import modes =====

    [Fact]
    public async Task Import_install_runs_repro_and_persists()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var controller = BuildController();
        var result = await controller.Import("install", null, BuildFormFile(bytes), default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var install = Assert.IsType<ImportInstallResult>(created.Value);
        Assert.Equal(SpecBundleStatus.Installed, install.Status);

        using var read = new AppDbContext(_options);
        var row = await read.SpecBundles.FirstAsync(b => b.Id == install.Id);
        Assert.NotNull(row.LastReproCheckAt);
        Assert.False(string.IsNullOrEmpty(row.LastReproCheckResultJson));
        Assert.Equal(_clock.UtcNow, row.LastReproCheckAt);
    }

    [Fact]
    public async Task Import_install_with_tampered_bundle_returns_400()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();

        // Tamper: rewrite a non-manifest entry's bytes so the parser's
        // SHA check fails. We bake a corrupt copy of bpmn.xml.
        var tampered = TamperWithEntry(bytes, "bpmn.xml", "<garbage />");

        var controller = BuildController();
        var result = await controller.Import("install", null, BuildFormFile(tampered), default);
        Assert.IsType<BadRequestObjectResult>(result);

        // Nothing persisted.
        using var read = new AppDbContext(_options);
        Assert.Empty(read.SpecBundles);
    }

    [Fact]
    public async Task Import_draft_does_not_persist()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var controller = BuildController();

        var result = await controller.Import("draft", null, BuildFormFile(bytes), default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var draft = Assert.IsType<ImportDraftResult>(ok.Value);
        Assert.Equal("LEAVE", draft.Manifest.FlowCode);
        Assert.True(draft.Validation.Valid);

        using var read = new AppDbContext(_options);
        Assert.Empty(read.SpecBundles);
    }

    [Fact]
    public async Task Import_idempotent_reimport_returns_409_with_existing_id()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var first = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var firstId = ((ImportInstallResult)((CreatedAtActionResult)first).Value!).Id;

        var second = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var conflict = Assert.IsType<ConflictObjectResult>(second);
        // Anonymous body — pull id via reflection.
        var idProp = conflict.Value!.GetType().GetProperty("id");
        Assert.NotNull(idProp);
        Assert.Equal(firstId, (Guid)idProp!.GetValue(conflict.Value)!);
    }

    // ===== Repro re-check =====

    [Fact]
    public async Task ReproCheck_updates_LastReproCheckAt()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        // Reset the LastReproCheckAt to a sentinel so we can confirm the
        // re-check actually wrote a fresh value.
        using (var db = new AppDbContext(_options))
        {
            var row = await db.SpecBundles.FirstAsync(b => b.Id == install.Id);
            row.LastReproCheckAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var result = await BuildController().ReproCheck(install.Id, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<ReproReport>(ok.Value);
        Assert.Equal(ReproStatus.Pass, report.OverallStatus);

        using var verify = new AppDbContext(_options);
        var refreshed = await verify.SpecBundles.AsNoTracking().FirstAsync(b => b.Id == install.Id);
        Assert.Equal(_clock.UtcNow, refreshed.LastReproCheckAt);
    }

    // ===== Delete soft-deletes =====

    [Fact]
    public async Task Delete_soft_deletes_the_bundle()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().Delete(install.Id, default);
        Assert.IsType<NoContentResult>(result);

        using var read = new AppDbContext(_options);
        var row = await read.SpecBundles.FirstAsync(b => b.Id == install.Id);
        Assert.Equal(SpecBundleStatus.SoftDeleted, row.Status);
    }

    [Fact]
    public async Task Delete_returns_404_when_not_found()
    {
        var result = await BuildController().Delete(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ===== PR-I7 §8.3 / §9.3 — Build (in-memory wizard payload → bundle) =====

    [Fact]
    public async Task Build_persists_pending_bundle_and_returns_id()
    {
        // Use the same LEAVE fixture pieces the import path uses; the body
        // mirrors what the wizard will assemble from the DraftSpec.
        var spec = await BundleTestFactory.LoadLeaveSpecAsync();
        var req = new FlowLibraryBuildRequest(
            SpecJson: spec,
            BpmnXml: "<bpmn:definitions />",
            SampleOrg: BundleTestFactory.DefaultOrg(),
            TestCases: BundleTestFactory.DefaultCases());

        var controller = BuildController();
        var result = await controller.Build(req, default);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<FlowLibraryBuildResult>(created.Value);
        Assert.Equal(SpecBundleStatus.Pending, body.Status);
        Assert.False(string.IsNullOrEmpty(body.ManifestChecksum));

        using var read = new AppDbContext(_options);
        var row = await read.SpecBundles.FirstAsync(b => b.Id == body.Id);
        Assert.Equal("LEAVE", row.FlowCode);
        Assert.Equal(SpecBundleStatus.Pending, row.Status);
        Assert.Equal(body.ManifestChecksum, row.ManifestChecksum);
        Assert.NotNull(row.ZipBlob);
        Assert.True(row.ZipBlob.Length > 0);
        // No repro yet — wizard hits the dedicated button.
        Assert.Null(row.LastReproCheckAt);
    }

    [Fact]
    public async Task Build_idempotent_on_manifest_checksum_returns_existing_id()
    {
        var spec = await BundleTestFactory.LoadLeaveSpecAsync();
        var req = new FlowLibraryBuildRequest(
            SpecJson: spec,
            BpmnXml: "<bpmn:definitions />",
            SampleOrg: BundleTestFactory.DefaultOrg(),
            TestCases: BundleTestFactory.DefaultCases());

        var first = await BuildController().Build(req, default);
        var firstBody = Assert.IsType<FlowLibraryBuildResult>(((CreatedAtActionResult)first).Value);

        // The clock is fixed (StubClock), so a second build with the same
        // payload reproduces the same manifest bytes → same checksum.
        var second = await BuildController().Build(req, default);
        var ok = Assert.IsType<OkObjectResult>(second);
        var secondBody = Assert.IsType<FlowLibraryBuildResult>(ok.Value);
        Assert.Equal(firstBody.Id, secondBody.Id);

        using var read = new AppDbContext(_options);
        Assert.Single(read.SpecBundles);
    }

    [Fact]
    public async Task Build_rejects_payload_with_no_test_cases_with_400()
    {
        var spec = await BundleTestFactory.LoadLeaveSpecAsync();
        var req = new FlowLibraryBuildRequest(
            SpecJson: spec,
            BpmnXml: "<bpmn:definitions />",
            SampleOrg: BundleTestFactory.DefaultOrg(),
            TestCases: Array.Empty<TestCaseSnapshot>());

        var result = await BuildController().Build(req, default);
        Assert.IsType<BadRequestObjectResult>(result);

        using var read = new AppDbContext(_options);
        Assert.Empty(read.SpecBundles);
    }

    // ===== PR-I7 §8.1 — Hydration (saved bundle → import-draft shape) =====

    [Fact]
    public async Task Hydration_returns_import_draft_shape_for_saved_bundle()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        var result = await BuildController().Hydration(install.Id, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var draft = Assert.IsType<ImportDraftResult>(ok.Value);

        Assert.Equal("LEAVE", draft.Manifest.FlowCode);
        Assert.True(draft.Validation.Valid);
        Assert.NotEmpty(draft.SampleOrg.Users);
        Assert.NotEmpty(draft.TestCases);
    }

    [Fact]
    public async Task Hydration_returns_404_for_missing_bundle()
    {
        var result = await BuildController().Hydration(Guid.NewGuid(), default);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Hydration_returns_404_for_soft_deleted_bundle()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var importResult = await BuildController().Import("install", null, BuildFormFile(bytes), default);
        var install = Assert.IsType<ImportInstallResult>(((CreatedAtActionResult)importResult).Value);

        await BuildController().Delete(install.Id, default);

        var result = await BuildController().Hydration(install.Id, default);
        Assert.IsType<NotFoundResult>(result);
    }

    // ===== harness =====

    private FlowLibraryController BuildController()
    {
        var db = new AppDbContext(_options);
        var parser = new BundleParser(NullLogger<BundleParser>.Instance);
        var validator = new BundleValidator();
        var loader = new BundleRuntimeLoader(db, NullLogger<BundleRuntimeLoader>.Instance);

        // Build the runtime against a separate context — same shape as the
        // BundleReproducibilityRunnerTests harness — so its tracker stays
        // independent from the controller's reads/writes during repro.
        var runtimeDb = new AppDbContext(_options);
        var orgReader = new OrgChartReader(runtimeDb);
        var auditor = new NoOpAuditor();
        var resolver = new ActorResolver(orgReader, auditor, NullLogger<ActorResolver>.Instance);
        var delegation = new StubDelegationService();
        var notifier = new StubNotificationDispatcher();
        var evaluator = new CelNetExpressionEvaluator(_clock);
        var loaderStub = new NullSpecLoader();
        var runtime = new ProcessRuntime(runtimeDb, _clock, loaderStub, resolver, delegation, notifier,
            evaluator, NullLogger<ProcessRuntime>.Instance);
        var runner = new BundleReproducibilityRunner(
            new AppDbContext(_options), runtime, NullLogger<BundleReproducibilityRunner>.Instance);

        var bundleBuilder = BundleTestFactory.NewBuilder();
        var controller = new FlowLibraryController(
            db, parser, validator, loader, runner, bundleBuilder, _clock,
            NullLogger<FlowLibraryController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = HttpContextFor(AdminId) };
        return controller;
    }

    private static IFormFile BuildFormFile(byte[] bytes, string fileName = "bundle.zip")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/zip",
        };
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

    /// <summary>
    /// Replace the bytes of a single zip entry with arbitrary text WITHOUT
    /// updating the manifest's recorded SHA-256 — guarantees the parser's
    /// tamper-detection trips when the controller re-parses.
    /// </summary>
    private static byte[] TamperWithEntry(byte[] originalZip, string entryPath, string newContent)
    {
        var ms = new MemoryStream();
        ms.Write(originalZip, 0, originalZip.Length);
        ms.Position = 0;

        // Re-open writable: copy out, mutate the target entry, write back.
        var output = new MemoryStream();
        using (var input = new MemoryStream(originalZip, writable: false))
        using (var inArchive = new ZipArchive(input, ZipArchiveMode.Read))
        using (var outArchive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in inArchive.Entries)
            {
                var newEntry = outArchive.CreateEntry(entry.FullName);
                using var dst = newEntry.Open();
                if (entry.FullName == entryPath)
                {
                    var data = System.Text.Encoding.UTF8.GetBytes(newContent);
                    dst.Write(data, 0, data.Length);
                }
                else
                {
                    using var src = entry.Open();
                    src.CopyTo(dst);
                }
            }
        }
        return output.ToArray();
    }
}

internal sealed class NoOpAuditor : IActorResolutionAuditor
{
    public Task WriteAsync(Bpm.Domain.Spec.ActorRef actor, Bpm.Domain.Spec.ResolutionContext ctx,
        Bpm.Domain.Spec.ResolutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class NullSpecLoader : ISpecLoader
{
    public Task<string?> LoadAsync(string tenantCode, string specCode, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

internal sealed class StubNotificationDispatcher : INotificationDispatcher
{
    public Task DispatchAsync(SpecSnapshot snapshot, string trigger, NotificationContext ctx,
        CancellationToken ct = default)
        => Task.CompletedTask;
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Api.Common;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Spec;
using Bpm.Domain.Spec.Bundle;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Api.Admin.FlowLibrary;

/// <summary>
/// Flow Library REST surface (PR-I5). One controller fronts the entire
/// SpecBundle lifecycle the admin UI needs:
///
/// <list type="bullet">
///   <item><description>List + detail reads (no zip blob in the JSON paths).</description></item>
///   <item><description>Streaming endpoints for the raw zip and individual files
///   inside it (the Detail screen's View tab loads each file lazily).</description></item>
///   <item><description>Multipart import that runs parse + validate (+ optional
///   repro install) inside the request — repro is synchronous so the UI gets
///   a deterministic 201 result without polling glue.</description></item>
///   <item><description>On-demand repro re-check + soft delete.</description></item>
/// </list>
///
/// <para>
/// Auth is admin-role only. Persistence is direct EF reads/writes against
/// <see cref="AppDbContext.SpecBundles"/> — no application service layer
/// because the operations are essentially CRUD around the bundle blob plus
/// the bundle pipeline (parser / validator / runtime loader / repro
/// runner) we already have wired in DI.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/flow-library")]
[Authorize(Roles = "admin")]
public sealed class FlowLibraryController(
    AppDbContext db,
    IBundleParser parser,
    IBundleValidator validator,
    IBundleRuntimeLoader runtimeLoader,
    IBundleReproducibilityRunner reproRunner,
    IClock clock,
    ILogger<FlowLibraryController> logger) : BpmControllerBase
{
    // Match BundleBuilder + BundleParser serializer settings so manifest
    // bytes round-trip cleanly (camelCase, enums-as-strings) and so
    // ImportDraft / repro-check responses match the on-disk bundle shape
    // the UI already knows how to render.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>List all bundles for a tenant. Excludes SoftDeleted.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<FlowLibraryItemDto>> List(
        [FromQuery] string? tenantCode,
        CancellationToken ct)
    {
        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;

        // Pull rows then project — ManifestJson parse + condensed-summary
        // formatting is cheap in-memory and trivially clearer than EF
        // expression-tree gymnastics.
        var rows = await db.SpecBundles
            .AsNoTracking()
            .Where(b => b.TenantCode == tc && b.Status != SpecBundleStatus.SoftDeleted)
            .ToListAsync(ct);

        var items = new List<FlowLibraryItemDto>(rows.Count);
        foreach (var b in rows)
        {
            var exportedAt = TryReadExportedAt(b.ManifestJson);
            var summary = TrySummarizeRepro(b.LastReproCheckResultJson);
            items.Add(new FlowLibraryItemDto(
                b.Id,
                b.FlowCode,
                b.FlowVersion,
                b.Status,
                b.ManifestChecksum,
                b.ParentManifestChecksum,
                exportedAt,
                b.LastReproCheckAt,
                summary));
        }

        return items
            .OrderByDescending(i => i.ExportedAt)
            .ToList();
    }

    /// <summary>Bundle metadata + parsed manifest + last repro report.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlowLibraryDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();

        var manifest = JsonSerializer.Deserialize<BundleManifest>(b.ManifestJson, JsonOpts)
            ?? throw new InvalidOperationException($"persisted manifest for bundle {id} failed to deserialize");

        var report = string.IsNullOrEmpty(b.LastReproCheckResultJson)
            ? null
            : JsonSerializer.Deserialize<ReproReport>(b.LastReproCheckResultJson!, JsonOpts);

        var exportedAt = TryReadExportedAt(b.ManifestJson);
        var summary = TrySummarizeRepro(b.LastReproCheckResultJson);
        var item = new FlowLibraryItemDto(
            b.Id, b.FlowCode, b.FlowVersion, b.Status, b.ManifestChecksum, b.ParentManifestChecksum,
            exportedAt, b.LastReproCheckAt, summary);

        return new FlowLibraryDetailDto(item, manifest, report);
    }

    /// <summary>Stream the raw zip blob with attachment disposition.</summary>
    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();
        var fileName = $"{b.FlowCode}_v{b.FlowVersion}.zip";
        return File(b.ZipBlob, "application/zip", fileName);
    }

    /// <summary>
    /// Stream a single file pulled out of the bundle zip. Path traversal
    /// hardening: the requested path must (a) lack ".." segments, (b) not
    /// be absolute, and (c) be present in the manifest's <c>Files</c>
    /// list — which is exhaustively trusted (the parser already verified
    /// every listed file's SHA on persist).
    /// </summary>
    [HttpGet("{id:guid}/files/{*path}")]
    public async Task<IActionResult> GetFile(Guid id, string path, CancellationToken ct)
    {
        if (!IsSafeRelativePath(path)) return BadRequest(new { error = "path is not a safe relative path" });

        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();

        var manifest = JsonSerializer.Deserialize<BundleManifest>(b.ManifestJson, JsonOpts);
        if (manifest is null) return NotFound();

        // Allow manifest.json itself even though it's not in Files[] (per
        // builder convention) — the View tab needs to render it too.
        var allowed = string.Equals(path, "manifest.json", StringComparison.Ordinal)
            || manifest.Files.Any(f => string.Equals(f.Path, path, StringComparison.Ordinal));
        if (!allowed) return NotFound();

        // Decompress just the one entry into memory. Bundle files are
        // small (the largest is bpmn.xml, KBs not MBs) so streaming
        // through ZipArchive's per-entry Stream is fine.
        using var ms = new MemoryStream(b.ZipBlob, writable: false);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);
        if (entry is null) return NotFound();

        byte[] bytes;
        using (var es = entry.Open())
        {
            using var copy = new MemoryStream();
            await es.CopyToAsync(copy, ct);
            bytes = copy.ToArray();
        }

        return File(bytes, ContentTypeFor(path));
    }

    /// <summary>
    /// Multipart upload of a bundle .zip. <c>mode=draft</c> parses +
    /// validates and returns the hydrated draft payload without persisting
    /// — used by the wizard's "open as 9-stepper" flow. <c>mode=install</c>
    /// persists, runs reproducibility, and stores the result.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // bundles are normally KBs; 50MB is generous headroom
    public async Task<IActionResult> Import(
        [FromQuery] string mode,
        [FromQuery] string? tenantCode,
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "file is required" });

        var modeNormalized = (mode ?? "install").Trim().ToLowerInvariant();
        if (modeNormalized != "install" && modeNormalized != "draft")
            return BadRequest(new { error = "mode must be 'install' or 'draft'" });

        // Buffer the upload — we need the bytes twice (parse + persist) and
        // need to know the size up front for the SpecBundle row.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        ParsedBundle parsed;
        try
        {
            using var parseStream = new MemoryStream(bytes, writable: false);
            parsed = await parser.ParseAsync(parseStream, ct);
        }
        catch (BundleParseException ex)
        {
            // Tampered / structurally bad zip — surface the parser's
            // message verbatim so the UI can show "checksum mismatch on
            // forms/leave_apply.json" etc.
            return BadRequest(new { error = "parse failed", detail = ex.Message, errors = ex.Errors });
        }

        var validation = validator.Validate(parsed);

        if (modeNormalized == "draft")
        {
            // Draft mode never persists; the UI uses the payload to seed
            // the 9-step wizard's draft state.
            return Ok(new ImportDraftResult(
                parsed.Manifest,
                parsed.SpecJson,
                parsed.SampleOrg,
                parsed.TestCases,
                validation));
        }

        // install mode
        if (!validation.Valid)
            return BadRequest(new { error = "validation failed", validation });

        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;

        // Idempotent re-import: the same checksum maps to the existing
        // row (the unique index on ManifestChecksum guarantees this).
        var existing = await db.SpecBundles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ManifestChecksum == parsed.ManifestChecksum, ct);
        if (existing is not null)
        {
            return Conflict(new { id = existing.Id, status = existing.Status });
        }

        var manifestJson = JsonSerializer.Serialize(parsed.Manifest, JsonOpts);

        var bundle = new SpecBundle
        {
            Id = Guid.NewGuid(),
            TenantCode = tc,
            FlowCode = parsed.Manifest.FlowCode,
            FlowVersion = parsed.Manifest.FlowVersion,
            ManifestChecksum = parsed.ManifestChecksum,
            ParentManifestChecksum = parsed.Manifest.Parent,
            ManifestJson = manifestJson,
            ZipBlob = bytes,
            Status = SpecBundleStatus.Pending,
        };
        db.SpecBundles.Add(bundle);
        await db.SaveChangesAsync(ct);

        // Run reproducibility synchronously. The bundled LEAVE-style cases
        // resolve in well under a second on the in-memory SQLite path —
        // the runner walks each test case to completion, so total cost is
        // O(test-cases × steps-per-case). For real bundles this is
        // typically a handful of cases; if a future bundle ships dozens
        // we'd push this onto a background worker (the entity already
        // carries Status=Pending so the UI can poll).
        ReproReport? report = null;
        string? reproError = null;
        try
        {
            await using var handle = await runtimeLoader.LoadAsync(parsed, ct);
            report = await reproRunner.RunAsync(handle, parsed.TestCases, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repro check failed for newly-imported bundle {Id}", bundle.Id);
            reproError = ex.Message;
        }

        // The bundle row was saved with a different DbContext snapshot above
        // — re-fetch on the same context so the status update lands
        // unambiguously rather than fighting whatever other entities the
        // runtime loader's seed pass may have attached to the tracker.
        var tracked = await db.SpecBundles.FirstAsync(b => b.Id == bundle.Id, ct);
        if (report is null)
        {
            tracked.Status = SpecBundleStatus.Failed;
            tracked.LastReproCheckAt = clock.UtcNow;
            tracked.LastReproCheckResultJson = JsonSerializer.Serialize(new { error = reproError }, JsonOpts);
            await db.SaveChangesAsync(ct);
            return CreatedAtAction(nameof(GetById), new { id = tracked.Id },
                new ImportInstallResult(tracked.Id, tracked.Status, null));
        }

        tracked.Status = report.OverallStatus == ReproStatus.Pass
            ? SpecBundleStatus.Installed
            : SpecBundleStatus.Failed;
        tracked.LastReproCheckAt = clock.UtcNow;
        tracked.LastReproCheckResultJson = JsonSerializer.Serialize(report, JsonOpts);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = tracked.Id },
            new ImportInstallResult(tracked.Id, tracked.Status, report));
    }

    /// <summary>Re-run reproducibility on an already-imported bundle.</summary>
    [HttpPost("{id:guid}/repro-check")]
    public async Task<IActionResult> ReproCheck(Guid id, CancellationToken ct)
    {
        // Quick existence check — read with AsNoTracking so the upcoming
        // BundleRuntimeLoader cleanup (which calls ChangeTracker.Clear on
        // its DbContext) doesn't strand our update.
        var exists = await db.SpecBundles
            .AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Status != SpecBundleStatus.SoftDeleted, ct);
        if (!exists) return NotFound();

        var blob = await db.SpecBundles
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.ZipBlob)
            .FirstAsync(ct);

        ParsedBundle parsed;
        using (var parseStream = new MemoryStream(blob, writable: false))
        {
            parsed = await parser.ParseAsync(parseStream, ct);
        }

        ReproReport? report = null;
        string? reproError = null;
        try
        {
            await using var handle = await runtimeLoader.LoadAsync(parsed, ct);
            report = await reproRunner.RunAsync(handle, parsed.TestCases, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repro re-check failed for bundle {Id}", id);
            reproError = ex.Message;
        }

        // Re-fetch + update on a fresh tracker — the loader's cleanup
        // path detaches everything on the controller's DbContext when
        // the using-block disposes.
        var tracked = await db.SpecBundles.FirstAsync(x => x.Id == id, ct);
        if (report is null)
        {
            tracked.Status = SpecBundleStatus.Failed;
            tracked.LastReproCheckAt = clock.UtcNow;
            tracked.LastReproCheckResultJson = JsonSerializer.Serialize(new { error = reproError }, JsonOpts);
            await db.SaveChangesAsync(ct);
            return StatusCode(500, new { error = "repro failed", detail = reproError });
        }

        tracked.Status = report.OverallStatus == ReproStatus.Pass
            ? SpecBundleStatus.Installed
            : SpecBundleStatus.Failed;
        tracked.LastReproCheckAt = clock.UtcNow;
        tracked.LastReproCheckResultJson = JsonSerializer.Serialize(report, JsonOpts);
        await db.SaveChangesAsync(ct);

        return Ok(report);
    }

    /// <summary>Soft-delete (Status -> SoftDeleted). Returns 204.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var bundle = await db.SpecBundles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (bundle is null || bundle.Status == SpecBundleStatus.SoftDeleted) return NotFound();
        bundle.Status = SpecBundleStatus.SoftDeleted;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ===== helpers =====

    /// <summary>
    /// Defensive path check before we trust the route value. Matches the
    /// safety bar described in tasks.md §6.5: no <c>..</c>, no leading
    /// slash, no absolute paths. We additionally normalise back-slashes
    /// to forward-slashes and reject any segment that is exactly <c>..</c>
    /// — string-Contains is too blunt because legitimate filenames could
    /// contain "..".
    /// </summary>
    internal static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (Path.IsPathRooted(path)) return false;
        var normalized = path.Replace('\\', '/');
        foreach (var seg in normalized.Split('/'))
        {
            if (seg == "..") return false;
            if (seg.Length == 0) return false; // double-slash
        }
        return true;
    }

    private static string ContentTypeFor(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".json" => "application/json",
            ".md" => "text/markdown",
            ".xml" => "application/xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            ".txt" => "text/plain",
            _ => "application/octet-stream",
        };
    }

    private static DateTime TryReadExportedAt(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            if (doc.RootElement.TryGetProperty("exportedAt", out var el)
                && el.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(el.GetString(), out var dto))
            {
                return dto.UtcDateTime;
            }
        }
        catch (JsonException) { }
        return DateTime.MinValue;
    }

    /// <summary>
    /// Condense the persisted ReproReport into a "PASS 3/3" / "FAIL 2/3"
    /// string for the list view. Returns null when no report has been
    /// stored yet (e.g. a freshly-uploaded Pending bundle).
    /// </summary>
    private static string? TrySummarizeRepro(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            // Synthetic { error } payload from a failed run — surface a
            // distinct marker so the UI doesn't try to count cases.
            if (doc.RootElement.TryGetProperty("error", out _)
                && !doc.RootElement.TryGetProperty("cases", out _))
            {
                return "ERROR";
            }
            if (!doc.RootElement.TryGetProperty("cases", out var cases)
                || cases.ValueKind != JsonValueKind.Array)
                return null;

            var total = cases.GetArrayLength();
            var passed = 0;
            foreach (var c in cases.EnumerateArray())
            {
                if (c.TryGetProperty("status", out var s)
                    && s.ValueKind == JsonValueKind.String
                    && string.Equals(s.GetString(), "pass", StringComparison.OrdinalIgnoreCase))
                {
                    passed++;
                }
            }
            var label = passed == total ? "PASS" : "FAIL";
            return $"{label} {passed}/{total}";
        }
        catch (JsonException) { return null; }
    }
}

// ===== DTOs =====

public sealed record FlowLibraryItemDto(
    Guid Id,
    string FlowCode,
    int FlowVersion,
    SpecBundleStatus Status,
    string ManifestChecksum,
    string? ParentManifestChecksum,
    DateTime ExportedAt,
    DateTime? LastReproCheckAt,
    string? LastReproCheckSummary);

public sealed record FlowLibraryDetailDto(
    FlowLibraryItemDto Summary,
    BundleManifest Manifest,
    ReproReport? LastReproReport);

public sealed record ImportDraftResult(
    BundleManifest Manifest,
    JsonElement SpecJson,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot> TestCases,
    BundleValidationResult Validation);

public sealed record ImportInstallResult(
    Guid Id,
    SpecBundleStatus Status,
    ReproReport? ReproReport);

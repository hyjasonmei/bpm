using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Api.Common;
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
/// Flow Library REST surface. Fronts the SpecBundle lifecycle: list +
/// detail reads, zip + per-file streaming, multipart import (parse +
/// validate + persist), wizard build, soft delete.
///
/// <para>
/// Reproducibility-runner endpoints (/repro-check + the bundle runtime
/// loader) were removed in the unify-user-store change — admin no longer
/// validates spec bundles by running them through ProcessRuntime; that
/// validation is now chef's responsibility downstream. The SpecBundle
/// row still carries LastReproCheckAt / LastReproCheckResultJson columns
/// for back-compat; new imports write null into them.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/flow-library")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class FlowLibraryController(
    AppDbContext db,
    IBundleParser parser,
    IBundleValidator validator,
    IBundleBuilder builder,
    ILogger<FlowLibraryController> logger) : BpmControllerBase
{
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

        var rows = await db.SpecBundles
            .AsNoTracking()
            .Where(b => b.TenantCode == tc && b.Status != SpecBundleStatus.SoftDeleted)
            .ToListAsync(ct);

        var items = new List<FlowLibraryItemDto>(rows.Count);
        foreach (var b in rows)
        {
            var exportedAt = TryReadExportedAt(b.ManifestJson);
            items.Add(new FlowLibraryItemDto(
                b.Id,
                b.FlowCode,
                b.FlowVersion,
                b.Status,
                b.ManifestChecksum,
                b.ParentManifestChecksum,
                exportedAt,
                b.LastReproCheckAt,
                null));
        }

        return items
            .OrderByDescending(i => i.ExportedAt)
            .ToList();
    }

    /// <summary>Bundle metadata + parsed manifest.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FlowLibraryDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();

        var manifest = JsonSerializer.Deserialize<BundleManifest>(b.ManifestJson, JsonOpts)
            ?? throw new InvalidOperationException($"persisted manifest for bundle {id} failed to deserialize");

        var exportedAt = TryReadExportedAt(b.ManifestJson);
        var item = new FlowLibraryItemDto(
            b.Id, b.FlowCode, b.FlowVersion, b.Status, b.ManifestChecksum, b.ParentManifestChecksum,
            exportedAt, b.LastReproCheckAt, null);

        return new FlowLibraryDetailDto(item, manifest);
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
    /// be absolute, and (c) be present in the manifest's <c>Files</c> list.
    /// </summary>
    [HttpGet("{id:guid}/files/{*path}")]
    public async Task<IActionResult> GetFile(Guid id, string path, CancellationToken ct)
    {
        if (!IsSafeRelativePath(path)) return BadRequest(new { error = "path is not a safe relative path" });

        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();

        var manifest = JsonSerializer.Deserialize<BundleManifest>(b.ManifestJson, JsonOpts);
        if (manifest is null) return NotFound();

        var allowed = string.Equals(path, "manifest.json", StringComparison.Ordinal)
            || manifest.Files.Any(f => string.Equals(f.Path, path, StringComparison.Ordinal));
        if (!allowed) return NotFound();

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
    /// validates and returns the hydrated draft payload without persisting.
    /// <c>mode=install</c> persists with Status=Installed; the chef downstream
    /// is responsible for validating the spec by writing + running code.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)]
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
            return BadRequest(new { error = "parse failed", detail = ex.Message, errors = ex.Errors });
        }

        var validation = validator.Validate(parsed);

        if (modeNormalized == "draft")
        {
            return Ok(new ImportDraftResult(
                parsed.Manifest,
                parsed.SpecJson,
                parsed.SampleOrg,
                parsed.TestCases,
                validation));
        }

        if (!validation.Valid)
            return BadRequest(new { error = "validation failed", validation });

        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;

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
            Status = SpecBundleStatus.Installed,
        };
        db.SpecBundles.Add(bundle);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = bundle.Id },
            new ImportInstallResult(bundle.Id, bundle.Status));
    }

    /// <summary>
    /// Build a bundle from in-memory wizard state. Persists a SpecBundle row
    /// in <c>Pending</c> status; chef-downstream validation happens out of band.
    /// Idempotent on <c>ManifestChecksum</c> — re-posting an identical payload
    /// returns the existing row id with 200 OK.
    /// </summary>
    [HttpPost("build")]
    public async Task<IActionResult> Build([FromBody] FlowLibraryBuildRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { error = "body is required" });

        var buildReq = new BundleBuildRequest(
            DraftSpecJson: req.SpecJson,
            BpmnXml: req.BpmnXml ?? string.Empty,
            SampleOrg: req.SampleOrg,
            TestCases: req.TestCases ?? Array.Empty<TestCaseSnapshot>(),
            IncludeAssets: false,
            IncludeChatSnapshots: false,
            ParentSpecJson: null,
            SourceInstanceId: string.IsNullOrWhiteSpace(req.SourceInstanceId) ? "default" : req.SourceInstanceId!);

        byte[] zipBytes;
        try
        {
            zipBytes = await builder.BuildAsync(buildReq, ct);
        }
        catch (BundleBuildException ex)
        {
            return BadRequest(new { error = "build failed", errors = ex.Errors });
        }

        ParsedBundle parsed;
        using (var ms = new MemoryStream(zipBytes, writable: false))
        {
            parsed = await parser.ParseAsync(ms, ct);
        }

        var tc = string.IsNullOrWhiteSpace(req.TenantCode) ? "default" : req.TenantCode!;

        var existing = await db.SpecBundles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ManifestChecksum == parsed.ManifestChecksum, ct);
        if (existing is not null)
        {
            return Ok(new FlowLibraryBuildResult(existing.Id, existing.Status, existing.ManifestChecksum));
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
            ZipBlob = zipBytes,
            Status = SpecBundleStatus.Pending,
        };
        db.SpecBundles.Add(bundle);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = bundle.Id },
            new FlowLibraryBuildResult(bundle.Id, bundle.Status, bundle.ManifestChecksum));
    }

    /// <summary>
    /// Re-hydrate a saved bundle into the import-draft payload shape. The
    /// wizard's "Open as draft" button on a Flow Library row hits this.
    /// </summary>
    [HttpGet("{id:guid}/hydration")]
    public async Task<IActionResult> Hydration(Guid id, CancellationToken ct)
    {
        var b = await db.SpecBundles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null || b.Status == SpecBundleStatus.SoftDeleted) return NotFound();

        ParsedBundle parsed;
        try
        {
            using var ms = new MemoryStream(b.ZipBlob, writable: false);
            parsed = await parser.ParseAsync(ms, ct);
        }
        catch (BundleParseException ex)
        {
            logger.LogError(ex, "Hydration failed to parse persisted bundle {Id}", id);
            return StatusCode(500, new { error = "bundle parse failed", detail = ex.Message });
        }

        var validation = validator.Validate(parsed);
        return Ok(new ImportDraftResult(
            parsed.Manifest,
            parsed.SpecJson,
            parsed.SampleOrg,
            parsed.TestCases,
            validation));
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

    internal static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith('/') || path.StartsWith('\\')) return false;
        if (Path.IsPathRooted(path)) return false;
        var normalized = path.Replace('\\', '/');
        foreach (var seg in normalized.Split('/'))
        {
            if (seg == "..") return false;
            if (seg.Length == 0) return false;
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
    BundleManifest Manifest);

public sealed record ImportDraftResult(
    BundleManifest Manifest,
    JsonElement SpecJson,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot> TestCases,
    BundleValidationResult Validation);

public sealed record ImportInstallResult(
    Guid Id,
    SpecBundleStatus Status);

/// <summary>
/// Wizard-side build payload. Mirrors the trimmed
/// <see cref="BundleBuildRequest"/> surface the admin UI needs.
/// </summary>
public sealed record FlowLibraryBuildRequest(
    JsonElement SpecJson,
    string? BpmnXml,
    SampleOrgSnapshot SampleOrg,
    IReadOnlyList<TestCaseSnapshot>? TestCases,
    string? TenantCode = null,
    string? SourceInstanceId = null);

public sealed record FlowLibraryBuildResult(
    Guid Id,
    SpecBundleStatus Status,
    string ManifestChecksum);

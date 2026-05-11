using System.Text.Json;
using Bpm.Api.Common;
using Bpm.Application.Process.Simulator;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bpm.Api.Admin.ProcessAdmin;

/// <summary>
/// Process Admin REST surface (PR-K1). Read-only endpoints powering the
/// "Definitions" section of the new Process Admin shell.
///
/// <para>
/// "Definition" is the operational view of a flow spec. Two stores back
/// it today:
/// <list type="bullet">
///   <item><description><see cref="AppDbContext.SpecBundles"/> rows
///   imported via the Flow Library — these are the canonical source going
///   forward.</description></item>
///   <item><description>Loose <c>sample_specs/*.json</c> files read by
///   <c>FileSystemSpecLoader</c> — still the runtime source until bundle
///   activation lands. They appear in the list so admins can see what the
///   loader will actually pick up.</description></item>
/// </list>
/// When a flow code exists in both stores, the bundle wins (deduped) — the
/// bundle is what authors see "going to ship" and the filesystem entry would
/// just confuse the list.
/// </para>
///
/// <para>
/// Auth mirrors <c>FlowLibraryController</c> — admin-role only. Finer
/// per-spec <c>flow_admin:&lt;code&gt;</c> roles are deferred to
/// <c>add-roles-and-permissions</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/admin/process-admin")]
[Authorize(Roles = "admin")]
public sealed class ProcessAdminController(
    AppDbContext db,
    IConfiguration configuration,
    IProcessSimulator simulator,
    ILogger<ProcessAdminController> logger) : BpmControllerBase
{
    private const string SourceBundle = "bundle";
    private const string SourceFilesystem = "filesystem";

    /// <summary>
    /// Combined list of installed bundles + loose filesystem specs for the
    /// Definitions list view. Filesystem entries whose flowCode already
    /// appears in bundles are suppressed (the bundle wins).
    /// </summary>
    [HttpGet("definitions")]
    public async Task<IReadOnlyList<FlowDefinitionDto>> ListDefinitions(
        [FromQuery] string? tenantCode,
        CancellationToken ct)
    {
        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;

        // Bundle side: latest version per FlowCode, ignoring SoftDeleted.
        var bundleRows = await db.SpecBundles
            .AsNoTracking()
            .Where(b => b.TenantCode == tc && b.Status != SpecBundleStatus.SoftDeleted)
            .ToListAsync(ct);

        var bundleLatest = bundleRows
            .GroupBy(b => b.FlowCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(b => b.FlowVersion).First())
            .ToList();

        var combined = new List<FlowDefinitionDto>(bundleLatest.Count);
        var bundleCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in bundleLatest)
        {
            bundleCodes.Add(b.FlowCode);
            combined.Add(new FlowDefinitionDto(
                Source: SourceBundle,
                FlowCode: b.FlowCode,
                Version: b.FlowVersion,
                Status: b.Status.ToString(),
                BundleId: b.Id,
                LastModifiedAt: b.LastReproCheckAt ?? TryReadExportedAt(b.ManifestJson)));
        }

        // Filesystem side: each *_v<n>.json under SampleSpecsDir whose
        // flowCode isn't already covered by a bundle.
        foreach (var fs in EnumerateFilesystemSpecs())
        {
            if (bundleCodes.Contains(fs.FlowCode)) continue;
            combined.Add(fs);
        }

        return combined
            .OrderByDescending(d => d.LastModifiedAt ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>
    /// Raw <c>spec.json</c> text for a flowCode (PR-K2 §3.6 — Designer load
    /// path). For filesystem-only definitions there's no bundle blob to read
    /// from; the Designer needs the spec contents to hydrate a DraftSpec.
    /// Resolution order matches what an admin would expect on the
    /// Definitions list:
    /// <list type="number">
    ///   <item><description>Latest non-deleted bundle for the flowCode →
    ///   the bundle's <c>spec.json</c> from inside the zip.</description></item>
    ///   <item><description>If no bundle, fall back to the filesystem spec
    ///   under <c>SampleSpecsDir</c>.</description></item>
    /// </list>
    /// Returns the spec text as <c>application/json</c>. The content is
    /// the spec file verbatim — no transformation. Wizard-side parsing /
    /// migration (`migrateDraft`) handles shape drift.
    /// </summary>
    [HttpGet("definitions/{flowCode}/spec")]
    public async Task<IActionResult> GetSpec(
        string flowCode,
        [FromQuery] string? tenantCode,
        CancellationToken ct)
    {
        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;
        if (string.IsNullOrWhiteSpace(flowCode)) return NotFound(new { error = "flowCode required" });

        // Bundle path: latest version wins.
        var bundleRows = await db.SpecBundles
            .AsNoTracking()
            .Where(b => b.TenantCode == tc
                        && b.FlowCode == flowCode
                        && b.Status != SpecBundleStatus.SoftDeleted)
            .ToListAsync(ct);
        var latest = bundleRows
            .OrderByDescending(b => b.FlowVersion)
            .FirstOrDefault();
        if (latest is not null)
        {
            string? specText = TryReadSpecJsonFromZip(latest.ZipBlob);
            if (specText is not null)
            {
                return Content(specText, "application/json");
            }
            // Bundle exists but the zip is missing spec.json — surface as 500
            // because the import path validates this; reaching here means the
            // row was stored corrupt.
            logger.LogError("ProcessAdmin: bundle {Id} for flow {Code} has no spec.json entry",
                latest.Id, flowCode);
            return StatusCode(500, new { error = "bundle is missing spec.json" });
        }

        // Filesystem fallback.
        var dir = ResolveSampleSpecsDir();
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            // FileSystemSpecLoader convention: <code_lowered>_v1.json. We
            // pick the highest-numbered version on disk so the Designer
            // hydrates the latest filesystem variant — matches what
            // ListDefinitions surfaces.
            var pattern = $"{flowCode.ToLowerInvariant()}_v*.json";
            var match = Directory.EnumerateFiles(dir, pattern)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match is not null)
            {
                var text = await System.IO.File.ReadAllTextAsync(match, ct);
                return Content(text, "application/json");
            }
        }

        return NotFound(new { error = $"no spec found for flowCode '{flowCode}'" });
    }

    /// <summary>
    /// All bundle versions for a flowCode, newest first. Filesystem-only
    /// flows return an empty list — versioning lives in the bundle store.
    /// </summary>
    [HttpGet("definitions/{flowCode}/versions")]
    public async Task<IReadOnlyList<FlowVersionDto>> ListVersions(
        string flowCode,
        [FromQuery] string? tenantCode,
        CancellationToken ct)
    {
        var tc = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode!;
        if (string.IsNullOrWhiteSpace(flowCode)) return Array.Empty<FlowVersionDto>();

        var rows = await db.SpecBundles
            .AsNoTracking()
            .Where(b => b.TenantCode == tc
                        && b.FlowCode == flowCode
                        && b.Status != SpecBundleStatus.SoftDeleted)
            .ToListAsync(ct);

        return rows
            .Select(b => new FlowVersionDto(
                BundleId: b.Id,
                FlowVersion: b.FlowVersion,
                ManifestChecksum: b.ManifestChecksum,
                ExportedAt: TryReadExportedAt(b.ManifestJson) ?? DateTime.MinValue,
                Status: b.Status,
                LastReproCheckAt: b.LastReproCheckAt))
            .OrderByDescending(v => v.FlowVersion)
            .ToList();
    }

    /// <summary>
    /// Process Simulator (PR-K3 §4.3) — drives the chosen flow forward
    /// against a sample form payload without persisting any
    /// ProcessInstance / ProcessTask / TaskHistory rows. Notifications that
    /// would have been dispatched are surfaced in the response.
    ///
    /// <para>
    /// Returns HTTP 200 even on simulation failure — a flow that doesn't
    /// exist or that errors mid-run is still a successful API call from
    /// the simulator's perspective. The caller inspects
    /// <see cref="SimulationResult.Success"/> to branch the UI.
    /// </para>
    /// </summary>
    [HttpPost("simulate")]
    public Task<SimulationResult> Simulate([FromBody] SimulateRequest req, CancellationToken ct)
        => simulator.SimulateAsync(
            new SimulationRequest(
                FlowCode: req.FlowCode,
                FormData: req.FormData,
                SampleOrg: req.SampleOrg,
                InitiatorUserId: req.InitiatorUserId),
            ct);

    // ===== helpers =====

    /// <summary>
    /// Walk the configured sample-specs directory once per call. Cheap
    /// enough — the dir holds a handful of files and the list endpoint is
    /// not on a hot path. Each *_v&lt;n&gt;.json is parsed lazily for
    /// meta.flowCode / meta.flowVersion so we surface the same identity
    /// the runtime loader uses. Files that don't parse are logged and
    /// skipped (we don't want one bad spec to take down the list).
    /// </summary>
    private IEnumerable<FlowDefinitionDto> EnumerateFilesystemSpecs()
    {
        var dir = ResolveSampleSpecsDir();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            FlowDefinitionDto? dto;
            try
            {
                dto = TryReadFilesystemSpec(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ProcessAdmin: failed to read sample spec {Path}", path);
                continue;
            }
            if (dto is not null) yield return dto;
        }
    }

    private static FlowDefinitionDto? TryReadFilesystemSpec(string path)
    {
        // Disambiguate from ControllerBase.File(...) — fully qualified.
        using var fs = System.IO.File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        if (!doc.RootElement.TryGetProperty("meta", out var meta)
            || meta.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var flowCode = meta.TryGetProperty("flowCode", out var fc) && fc.ValueKind == JsonValueKind.String
            ? fc.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(flowCode)) return null;

        var version = meta.TryGetProperty("flowVersion", out var fv) && fv.ValueKind == JsonValueKind.Number
            ? fv.GetInt32()
            : 1;

        var lastModified = System.IO.File.GetLastWriteTimeUtc(path);
        return new FlowDefinitionDto(
            Source: SourceFilesystem,
            FlowCode: flowCode!,
            Version: version,
            Status: null,
            BundleId: null,
            LastModifiedAt: lastModified);
    }

    private string ResolveSampleSpecsDir()
    {
        // Mirror FileSystemSpecLoader's resolution path so the list shows
        // exactly what the runtime loader would resolve.
        var configured = configuration["Bpm:SampleSpecsDir"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        return Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "sample_specs"));
    }

    /// <summary>
    /// Pull <c>spec.json</c> text out of a SpecBundle zip blob without
    /// invoking the BundleParser (which would also validate sample-org /
    /// test-cases and add a non-trivial cost). The Designer just needs the
    /// raw spec; per-file extraction is exact + cheap.
    /// </summary>
    private static string? TryReadSpecJsonFromZip(byte[] zipBlob)
    {
        if (zipBlob is null || zipBlob.Length == 0) return null;
        using var ms = new MemoryStream(zipBlob, writable: false);
        using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("spec.json");
        if (entry is null) return null;
        using var es = entry.Open();
        using var reader = new StreamReader(es);
        return reader.ReadToEnd();
    }

    private static DateTime? TryReadExportedAt(string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson)) return null;
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
        return null;
    }
}

// ===== DTOs =====

/// <summary>
/// Row in the Definitions list. <see cref="Source"/> is "bundle" or
/// "filesystem" — bundles carry a status + bundleId so the UI can deep-link
/// into the Flow Library; filesystem entries don't have either.
/// </summary>
public sealed record FlowDefinitionDto(
    string Source,
    string FlowCode,
    int Version,
    string? Status,
    Guid? BundleId,
    DateTime? LastModifiedAt);

public sealed record FlowVersionDto(
    Guid BundleId,
    int FlowVersion,
    string ManifestChecksum,
    DateTime ExportedAt,
    SpecBundleStatus Status,
    DateTime? LastReproCheckAt);

/// <summary>
/// Simulator request body. Mirrors <see cref="SimulationRequest"/>; the
/// dedicated DTO keeps the controller's public surface from leaking the
/// Application layer's record into the model-binding pipeline.
/// </summary>
public sealed record SimulateRequest(
    string FlowCode,
    JsonElement FormData,
    SampleOrgSnapshot? SampleOrg,
    Guid? InitiatorUserId);

using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bpm.SeedCli.Services;

/// <summary>
/// Shared bundle install primitive. Builds canonical zip bytes from a
/// <see cref="BundleBuildRequest"/> and persists a <see cref="SpecBundle"/>
/// row in <see cref="SpecBundleStatus.Pending"/>.
///
/// <para>
/// Idempotent on <c>ManifestChecksum</c>: re-running with byte-identical
/// inputs returns the existing row id without inserting a duplicate.
/// </para>
///
/// <para>
/// Distinct from the FlowLibraryController.Import path because no repro
/// run is triggered. Repro is slow + brittle for SeedCli (requires
/// per-spec test cases that drive the runtime end-to-end); the user runs
/// it manually via the Flow Library "Repro Check" button.
/// </para>
/// </summary>
public sealed class BundleInstaller(
    AppDbContext db,
    IBundleBuilder builder,
    IBundleParser parser)
{
    public sealed record InstallOutcome(Guid BundleId, string ManifestChecksum, bool AlreadyExisted);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public async Task<InstallOutcome> InstallAsync(
        string tenantCode,
        BundleBuildRequest req,
        CancellationToken ct = default)
    {
        var zipBytes = await builder.BuildAsync(req, ct);

        ParsedBundle parsed;
        using (var ms = new MemoryStream(zipBytes, writable: false))
        {
            parsed = await parser.ParseAsync(ms, ct);
        }

        // Idempotency on manifest checksum — same canonical zip = same row.
        var existing = await db.SpecBundles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ManifestChecksum == parsed.ManifestChecksum, ct);
        if (existing is not null)
        {
            return new InstallOutcome(existing.Id, existing.ManifestChecksum, AlreadyExisted: true);
        }

        var manifestJson = JsonSerializer.Serialize(parsed.Manifest, JsonOpts);
        var bundle = new SpecBundle
        {
            Id = Guid.NewGuid(),
            TenantCode = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode,
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

        return new InstallOutcome(bundle.Id, bundle.ManifestChecksum, AlreadyExisted: false);
    }
}

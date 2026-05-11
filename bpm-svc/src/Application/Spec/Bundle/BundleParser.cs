using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Domain.Spec.Bundle;
using Microsoft.Extensions.Logging;

namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Reference implementation of <see cref="IBundleParser"/>. Sequence:
///
/// <list type="number">
///   <item><description>Open the zip read-only with <c>leaveOpen: true</c>
///   so the caller still owns the underlying stream.</description></item>
///   <item><description>Locate <c>manifest.json</c> (case-sensitive). Missing
///   manifest → <see cref="BundleParseException"/>.</description></item>
///   <item><description>Reject any <c>bundleSchemaVersion</c> greater than
///   <see cref="BundleSchemaVersion.Current"/> — keeps forward-compat
///   boundaries explicit (newer bundles must be opened by a newer parser).</description></item>
///   <item><description>For every entry in <c>manifest.Files</c>, verify the
///   listed file exists in the zip and its SHA-256 matches the manifest's
///   recorded hash. Mismatch → <see cref="BundleParseException"/> (tamper
///   detection).</description></item>
///   <item><description>Decode each known kind into the
///   <see cref="ParsedBundle"/> shape. JSON files are read once and cloned
///   so the underlying <see cref="JsonDocument"/> can be disposed.</description></item>
///   <item><description>Surface unlisted zip entries in
///   <see cref="ParsedBundle.UnknownEntries"/> and log them at Info level
///   — never throw (forward-compat).</description></item>
/// </list>
/// </summary>
public sealed class BundleParser(ILogger<BundleParser> logger) : IBundleParser
{
    // Match BundleBuilder's serializer options so round-trip parses work.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public Task<ParsedBundle> ParseAsync(Stream zip, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(zip);

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Manifest is the only mandatory entry.
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new BundleParseException("manifest.json missing");

        var manifestBytes = ReadEntryBytes(manifestEntry);
        var manifestChecksum = ComputeSha256(manifestBytes);

        BundleManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BundleManifest>(manifestBytes, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new BundleParseException($"manifest.json is not valid JSON: {ex.Message}");
        }
        if (manifest is null)
            throw new BundleParseException("manifest.json deserialized to null");

        // 2. Schema version gate — newer than us → reject explicitly.
        if (manifest.BundleSchemaVersion > BundleSchemaVersion.Current)
            throw new BundleParseException($"unknown schema version {manifest.BundleSchemaVersion}");

        // 3. Hydrate each known kind. Collect into builders first so a
        //    single missing file aborts cleanly without partial state.
        var forms = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var notifications = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var testCases = new List<TestCaseSnapshot>();

        JsonElement specJson = default;
        string bpmnXml = string.Empty;
        string specMd = string.Empty;
        string? readme = null;
        string? walkthrough = null;
        string? changelog = null;
        JsonElement? sla = null;
        JsonElement? actors = null;
        SampleOrgSnapshot? sampleOrg = null;

        var listedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Files)
        {
            listedPaths.Add(entry.Path);
            var ze = archive.GetEntry(entry.Path)
                ?? throw new BundleParseException($"file '{entry.Path}' listed in manifest but not in zip");

            var bytes = ReadEntryBytes(ze);
            var actual = ComputeSha256(bytes);
            if (!string.Equals(actual, entry.Sha256, StringComparison.Ordinal))
            {
                throw new BundleParseException(
                    $"checksum mismatch for '{entry.Path}': expected {entry.Sha256}, got {actual}");
            }

            switch (entry.Kind)
            {
                case BundleFileKind.Spec:
                    specJson = ParseJsonClone(bytes, entry.Path);
                    break;
                case BundleFileKind.BpmnXml:
                    bpmnXml = Encoding.UTF8.GetString(bytes);
                    break;
                case BundleFileKind.SpecMd:
                    specMd = Encoding.UTF8.GetString(bytes);
                    break;
                case BundleFileKind.Readme:
                    readme = Encoding.UTF8.GetString(bytes);
                    break;
                case BundleFileKind.Walkthrough:
                    walkthrough = Encoding.UTF8.GetString(bytes);
                    break;
                case BundleFileKind.Changelog:
                    changelog = Encoding.UTF8.GetString(bytes);
                    break;
                case BundleFileKind.Form:
                    forms[ExtractIdFromPath(entry.Path, "forms/", ".json")] = ParseJsonClone(bytes, entry.Path);
                    break;
                case BundleFileKind.Notification:
                    notifications[ExtractIdFromPath(entry.Path, "notifications/", ".json")] = ParseJsonClone(bytes, entry.Path);
                    break;
                case BundleFileKind.Sla:
                    sla = ParseJsonClone(bytes, entry.Path);
                    break;
                case BundleFileKind.Actors:
                    actors = ParseJsonClone(bytes, entry.Path);
                    break;
                case BundleFileKind.SampleOrg:
                    sampleOrg = JsonSerializer.Deserialize<SampleOrgSnapshot>(bytes, JsonOpts)
                        ?? throw new BundleParseException($"sample-org.json deserialized to null");
                    break;
                case BundleFileKind.TestCase:
                    var tc = JsonSerializer.Deserialize<TestCaseSnapshot>(bytes, JsonOpts)
                        ?? throw new BundleParseException($"test-case '{entry.Path}' deserialized to null");
                    testCases.Add(tc);
                    break;
                case BundleFileKind.Asset:
                    assets[entry.Path] = bytes;
                    break;
                case BundleFileKind.Manifest:
                    // Defensive: per spec the manifest is NOT in its own files[],
                    // but if a future version ever changes that we don't want to
                    // silently swallow.
                    break;
                case BundleFileKind.Other:
                default:
                    // Unknown kind in a known-version manifest — warn but don't fail.
                    logger.LogWarning(
                        "Bundle entry '{Path}' has unrecognized kind {Kind}; skipping",
                        entry.Path, entry.Kind);
                    break;
            }
        }

        // 4. Forward-compat: anything in the zip that wasn't in the manifest
        //    (excluding manifest.json itself) is logged + surfaced, never an
        //    error. Newer-version bundles can ship additional file types and
        //    older parsers will gracefully skip them.
        var unknown = new List<string>();
        foreach (var ze in archive.Entries)
        {
            if (string.Equals(ze.FullName, "manifest.json", StringComparison.Ordinal))
                continue;
            if (listedPaths.Contains(ze.FullName))
                continue;
            // ZipArchive returns directory entries with trailing '/' on some
            // toolchains; ignore those — they hold no payload.
            if (ze.FullName.EndsWith('/'))
                continue;
            unknown.Add(ze.FullName);
            logger.LogInformation(
                "Bundle contains unlisted entry '{Path}' (size={Size}); ignored for forward-compat",
                ze.FullName, ze.Length);
        }

        if (sampleOrg is null)
            throw new BundleParseException("sample-org.json missing from manifest.files");

        return Task.FromResult(new ParsedBundle(
            Manifest: manifest,
            ManifestChecksum: manifestChecksum,
            SpecJson: specJson,
            BpmnXml: bpmnXml,
            SpecMd: specMd,
            Readme: readme,
            Walkthrough: walkthrough,
            Changelog: changelog,
            Forms: forms,
            Notifications: notifications,
            Sla: sla,
            Actors: actors,
            SampleOrg: sampleOrg,
            TestCases: testCases,
            Assets: assets,
            UnknownEntries: unknown));
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Parse JSON bytes and return a Cloned JsonElement so callers can hold
    /// it after we dispose the JsonDocument.
    /// </summary>
    private static JsonElement ParseJsonClone(byte[] bytes, string pathForError)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new BundleParseException($"file '{pathForError}' is not valid JSON: {ex.Message}");
        }
    }

    private static string ExtractIdFromPath(string path, string prefix, string suffix)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
            return path; // shouldn't happen given builder-side conventions; return raw path so caller sees something
        return path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
    }
}

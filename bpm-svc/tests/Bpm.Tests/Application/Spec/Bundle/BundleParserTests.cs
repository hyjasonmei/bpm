using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class BundleParserTests
{
    private static BundleParser NewParser() => new(NullLogger<BundleParser>.Instance);

    [Fact]
    public async Task RoundTrip_leave_bundle_hydrates_every_known_kind()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        using var ms = new MemoryStream(bytes);

        var parsed = await NewParser().ParseAsync(ms);

        Assert.Equal("LEAVE", parsed.Manifest.FlowCode);
        Assert.Equal(1, parsed.Manifest.FlowVersion);
        Assert.Equal(64, parsed.ManifestChecksum.Length); // SHA-256 hex
        Assert.Equal(parsed.ManifestChecksum, parsed.ManifestChecksum.ToUpperInvariant());

        // spec.json round-trips through JsonElement.Clone (so JsonDocument was disposed).
        Assert.Equal("LEAVE", parsed.SpecJson.GetProperty("meta").GetProperty("flowCode").GetString());

        // Companion text artifacts.
        Assert.False(string.IsNullOrEmpty(parsed.SpecMd));
        Assert.False(string.IsNullOrEmpty(parsed.Readme));
        Assert.False(string.IsNullOrEmpty(parsed.Walkthrough));
        Assert.Null(parsed.Changelog); // no parent supplied

        // Forms keyed by userTaskId (filename strip — "forms/X.json" -> "X").
        Assert.Contains("task_apply", parsed.Forms.Keys);
        Assert.Contains("task_hr_archive", parsed.Forms.Keys);
        Assert.Equal("task_apply", parsed.Forms["task_apply"].GetProperty("id").GetString());

        // Notifications similarly keyed.
        Assert.Contains("notify_assign_manager", parsed.Notifications.Keys);
        Assert.Contains("notify_complete", parsed.Notifications.Keys);

        // SLA + actors are JsonElement passthroughs.
        Assert.NotNull(parsed.Sla);
        Assert.NotNull(parsed.Actors);

        // SampleOrg + TestCases deserialised into typed snapshots.
        Assert.Equal(2, parsed.SampleOrg.Users.Count);
        Assert.Single(parsed.TestCases);
        Assert.Equal("tc_1", parsed.TestCases[0].Id);

        // No assets in the default build.
        Assert.Empty(parsed.Assets);
        Assert.Empty(parsed.UnknownEntries);
    }

    [Fact]
    public async Task Parse_throws_when_manifest_missing()
    {
        // Build a zip that has spec.json but no manifest — easiest way is to
        // construct one by hand instead of mutating a real bundle.
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("spec.json").Open();
            s.Write(Encoding.UTF8.GetBytes("{}"));
        }
        ms.Position = 0;

        var ex = await Assert.ThrowsAsync<BundleParseException>(() => NewParser().ParseAsync(ms));
        Assert.Contains("manifest.json missing", ex.Message);
    }

    /// <summary>3.6 — Tampered file: rewriting one entry's bytes after the
    /// manifest is sealed must surface as a checksum mismatch, not a
    /// silent corrupt-bundle pass.</summary>
    [Fact]
    public async Task Parse_throws_on_checksum_mismatch_after_tampering()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var tampered = MutateZipEntry(bytes, "forms/task_apply.json", _ => Encoding.UTF8.GetBytes("garbage payload"));

        using var ms = new MemoryStream(tampered);
        var ex = await Assert.ThrowsAsync<BundleParseException>(() => NewParser().ParseAsync(ms));
        Assert.Contains("checksum mismatch", ex.Message);
        Assert.Contains("forms/task_apply.json", ex.Message);
    }

    /// <summary>3.3 — Schema version newer than `Current` must hard-stop
    /// rather than try a best-effort parse.</summary>
    [Fact]
    public async Task Parse_throws_on_future_schema_version()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var bumped = MutateZipEntry(bytes, "manifest.json", original =>
        {
            var json = Encoding.UTF8.GetString(original);
            // The manifest is camelCase; replace the version number directly.
            json = json.Replace("\"bundleSchemaVersion\": 1", "\"bundleSchemaVersion\": 99");
            return Encoding.UTF8.GetBytes(json);
        });

        using var ms = new MemoryStream(bumped);
        var ex = await Assert.ThrowsAsync<BundleParseException>(() => NewParser().ParseAsync(ms));
        Assert.Contains("unknown schema version 99", ex.Message);
    }

    /// <summary>3.8 — Forward-compat: an unlisted file must not derail
    /// the parse; it gets surfaced via UnknownEntries for telemetry.</summary>
    [Fact]
    public async Task Parse_succeeds_with_unlisted_extra_file_and_reports_it()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        var withExtra = AppendUnlistedZipEntry(bytes, "policies/extra.json", Encoding.UTF8.GetBytes("{\"future\":true}"));

        using var ms = new MemoryStream(withExtra);
        var parsed = await NewParser().ParseAsync(ms);

        Assert.Contains("policies/extra.json", parsed.UnknownEntries);
        Assert.Equal("LEAVE", parsed.Manifest.FlowCode);
    }

    [Fact]
    public async Task Parse_throws_when_manifest_lists_a_missing_file()
    {
        var bytes = await BundleTestFactory.BuildLeaveBundleAsync();
        // Drop forms/task_apply.json but leave the manifest intact so it
        // still references the missing path.
        var dropped = DeleteZipEntry(bytes, "forms/task_apply.json");

        using var ms = new MemoryStream(dropped);
        var ex = await Assert.ThrowsAsync<BundleParseException>(() => NewParser().ParseAsync(ms));
        Assert.Contains("forms/task_apply.json", ex.Message);
        Assert.Contains("listed in manifest but not in zip", ex.Message);
    }

    // ---- zip helpers -------------------------------------------------

    /// <summary>
    /// Apply <paramref name="rewrite"/> to the named entry's bytes,
    /// returning a new byte[] containing the modified zip.
    /// </summary>
    private static byte[] MutateZipEntry(byte[] zipBytes, string path, Func<byte[], byte[]> rewrite)
    {
        var copy = (byte[])zipBytes.Clone();
        using var ms = new MemoryStream();
        ms.Write(copy);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"entry {path} not in zip");
            byte[] originalBytes;
            using (var rs = entry.Open())
            using (var bms = new MemoryStream())
            {
                rs.CopyTo(bms);
                originalBytes = bms.ToArray();
            }
            var newBytes = rewrite(originalBytes);
            entry.Delete();
            var fresh = zip.CreateEntry(path);
            using var ws = fresh.Open();
            ws.Write(newBytes, 0, newBytes.Length);
        }
        return ms.ToArray();
    }

    private static byte[] AppendUnlistedZipEntry(byte[] zipBytes, string path, byte[] content)
    {
        var copy = (byte[])zipBytes.Clone();
        using var ms = new MemoryStream();
        ms.Write(copy);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var fresh = zip.CreateEntry(path);
            using var ws = fresh.Open();
            ws.Write(content, 0, content.Length);
        }
        return ms.ToArray();
    }

    private static byte[] DeleteZipEntry(byte[] zipBytes, string path)
    {
        var copy = (byte[])zipBytes.Clone();
        using var ms = new MemoryStream();
        ms.Write(copy);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidOperationException($"entry {path} not in zip");
            entry.Delete();
        }
        return ms.ToArray();
    }
}

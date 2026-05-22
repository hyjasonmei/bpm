using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Admin.Domain.Bundle;

namespace Bpm.Admin.Application.Bundle;

/// <summary>
/// Reference implementation of <see cref="IBundleBuilder"/>. Sequence:
///
/// <list type="number">
///   <item><description>Validate inputs via <see cref="BundleBuildValidator"/>.</description></item>
///   <item><description>Stream every canonical file into a fresh <see cref="ZipArchive"/>.</description></item>
///   <item><description>Compute SHA-256 (uppercase hex) of each file's raw bytes
///   while writing, and accumulate them into <see cref="BundleManifest.Files"/>.</description></item>
///   <item><description>Write <c>manifest.json</c> last — the manifest itself is
///   NOT in its own <c>files[]</c> (chicken/egg).</description></item>
/// </list>
///
/// JSON written by the builder uses camelCase + indented formatting for
/// readability; <c>spec.json</c> alone is passed through verbatim
/// (<see cref="JsonElement.GetRawText"/>) so authoring intent — including
/// property order and whitespace — is preserved.
/// </summary>
public sealed class BundleBuilder(
    BundleBuildValidator validator,
    SpecMdRenderer specMdRenderer,
    WalkthroughRenderer walkthroughRenderer,
    ChangelogRenderer changelogRenderer,
    TimeProvider clock) : IBundleBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public Task<byte[]> BuildAsync(BundleBuildRequest req, CancellationToken ct = default)
    {
        validator.Validate(req);

        var entries = new List<BundleFileEntry>();
        using var ms = new MemoryStream();

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. spec.json — passthrough to preserve authoring intent.
            WriteText(archive, entries, "spec.json", req.DraftSpecJson.GetRawText(), BundleFileKind.Spec);

            // 2. bpmn.xml
            WriteText(archive, entries, "bpmn.xml", req.BpmnXml ?? string.Empty, BundleFileKind.BpmnXml);

            // 3. spec.md
            WriteText(archive, entries, "spec.md", specMdRenderer.Render(req.DraftSpecJson), BundleFileKind.SpecMd);

            // 4. README.md
            WriteText(archive, entries, "README.md", BuildReadme(req.DraftSpecJson), BundleFileKind.Readme);

            // 5. walkthrough.md
            var firstCase = req.TestCases.Count > 0 ? req.TestCases[0] : null;
            WriteText(archive, entries, "walkthrough.md", walkthroughRenderer.Render(req.DraftSpecJson, firstCase), BundleFileKind.Walkthrough);

            // 6. forms/{userTaskId}.json
            foreach (var (id, payload) in EnumerateUserTaskForms(req.DraftSpecJson))
            {
                WriteJson(archive, entries, $"forms/{id}.json", payload, BundleFileKind.Form);
            }

            // 7. notifications/{notificationId}.json
            foreach (var (id, payload) in EnumerateNotifications(req.DraftSpecJson))
            {
                WriteJson(archive, entries, $"notifications/{id}.json", payload, BundleFileKind.Notification);
            }

            // 8. sla.json
            WriteJson(archive, entries, "sla.json", ExtractSla(req.DraftSpecJson), BundleFileKind.Sla);

            // 9. actors.json
            WriteJson(archive, entries, "actors.json", ActorRefCollector.Collect(req.DraftSpecJson), BundleFileKind.Actors);

            // 10. sample-org.json
            WriteJson(archive, entries, "sample-org.json", req.SampleOrg, BundleFileKind.SampleOrg);

            // 11. test-cases/{caseId}.json
            foreach (var tc in req.TestCases)
            {
                WriteJson(archive, entries, $"test-cases/{tc.Id}.json", tc, BundleFileKind.TestCase);
            }

            // 12. CHANGELOG.md when a parent spec is provided.
            if (req.ParentSpecJson is { } parent)
            {
                WriteText(archive, entries, "CHANGELOG.md", changelogRenderer.Render(parent, req.DraftSpecJson), BundleFileKind.Changelog);
            }

            // 13. assets — opt-in; v1 has no asset source so we skip silently.
            //     PR-I6 / wizard wiring fills this in once a real diagram PNG is available.
            _ = req.IncludeAssets;
            _ = req.IncludeChatSnapshots;

            // 14. manifest.json — last, and NOT in its own files[].
            var (flowCode, flowVersion) = ExtractFlowMeta(req.DraftSpecJson);
            var manifest = new BundleManifest(
                BundleSchemaVersion: BundleSchemaVersion.Current,
                FlowCode: flowCode,
                FlowVersion: flowVersion,
                ExportedAt: clock.GetUtcNow(),
                SourceInstanceId: req.SourceInstanceId,
                Parent: null,
                Files: entries);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOpts);
            WriteRaw(archive, "manifest.json", Encoding.UTF8.GetBytes(manifestJson));
        }

        return Task.FromResult(ms.ToArray());
    }

    private static void WriteText(ZipArchive archive, List<BundleFileEntry> entries, string path, string content, BundleFileKind kind)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        WriteRaw(archive, path, bytes);
        entries.Add(new BundleFileEntry(path, ComputeSha256(bytes), bytes.LongLength, kind));
    }

    private static void WriteJson<T>(ZipArchive archive, List<BundleFileEntry> entries, string path, T payload, BundleFileKind kind)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        WriteRaw(archive, path, bytes);
        entries.Add(new BundleFileEntry(path, ComputeSha256(bytes), bytes.LongLength, kind));
    }

    private static void WriteRaw(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        // Pin entry timestamps so two builds with the same inputs + clock
        // produce byte-identical output (manifest hashes match across runs).
        entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash); // uppercase hex per spec
    }

    private static (string FlowCode, int FlowVersion) ExtractFlowMeta(JsonElement spec)
    {
        var code = "unknown";
        var ver = 1;
        if (spec.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            if (meta.TryGetProperty("flowCode", out var fc) && fc.ValueKind == JsonValueKind.String)
                code = fc.GetString() ?? code;
            if (meta.TryGetProperty("flowVersion", out var fv) && fv.ValueKind == JsonValueKind.Number)
                ver = fv.GetInt32();
        }
        return (code, ver);
    }

    private static string BuildReadme(JsonElement spec)
    {
        var meta = spec.TryGetProperty("meta", out var m) ? m : default;
        var flowName = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("flowName", out var fn) ? fn.GetString() ?? "(unnamed)" : "(unnamed)";
        var flowCode = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("flowCode", out var fc) ? fc.GetString() ?? "?" : "?";
        var flowVersion = meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty("flowVersion", out var fv) && fv.ValueKind == JsonValueKind.Number ? fv.GetInt32() : 1;

        var nodeCount = 0;
        var userTaskCount = 0;
        var approvalCount = 0;
        if (spec.TryGetProperty("flow", out var flow)
            && flow.TryGetProperty("nodes", out var nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in nodes.EnumerateArray())
            {
                nodeCount++;
                if (n.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var ts = t.GetString();
                    if (ts == "userTask") userTaskCount++;
                    else if (ts == "approval") approvalCount++;
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("# ").Append(flowName).Append(" (").Append(flowCode).Append(" v").Append(flowVersion).AppendLine(")");
        sb.AppendLine();
        sb.Append("This bundle packages the **").Append(flowName).AppendLine("** flow for portable installation.");
        sb.Append("It contains ").Append(nodeCount).Append(" flow nodes, including ")
          .Append(userTaskCount).Append(" user task(s) and ")
          .Append(approvalCount).AppendLine(" approval step(s).");
        sb.AppendLine();
        sb.AppendLine("See `spec.md` for the human-readable overview, `walkthrough.md` for the happy-path narration, and `manifest.json` for the canonical file list with SHA-256 checksums.");
        return sb.ToString();
    }

    private static IEnumerable<(string Id, object Payload)> EnumerateUserTaskForms(JsonElement spec)
    {
        if (!spec.TryGetProperty("userTasks", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var t in arr.EnumerateArray())
        {
            if (!t.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id)) continue;
            var fields = t.TryGetProperty("fields", out var fEl) ? CloneRaw(fEl) : "[]";
            var perms = t.TryGetProperty("permissions", out var pEl) ? CloneRaw(pEl) : "{}";
            yield return (id, new FormPayload(id, JsonDocument.Parse(fields).RootElement.Clone(), JsonDocument.Parse(perms).RootElement.Clone()));
        }
    }

    private static IEnumerable<(string Id, JsonElement Payload)> EnumerateNotifications(JsonElement spec)
    {
        if (!spec.TryGetProperty("notifications", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var n in arr.EnumerateArray())
        {
            if (!n.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id)) continue;
            yield return (id, n.Clone());
        }
    }

    private static JsonElement ExtractSla(JsonElement spec)
    {
        if (spec.TryGetProperty("sla", out var sla) && sla.ValueKind == JsonValueKind.Object)
            return sla.Clone();
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static string CloneRaw(JsonElement el) => el.GetRawText();

    private sealed record FormPayload(string Id, JsonElement Fields, JsonElement Permissions);
}

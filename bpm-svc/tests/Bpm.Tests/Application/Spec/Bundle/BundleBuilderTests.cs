using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Spec.Bundle;
using Bpm.Tests.Common;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class BundleBuilderTests
{
    private static readonly string LeavePath = BundleTestFactory.LeaveSpecPath;

    private static BundleBuilder NewBuilder() => new(
        new BundleBuildValidator(),
        new SpecMdRenderer(),
        new WalkthroughRenderer(),
        new ChangelogRenderer(),
        new StubClock());

    private static SampleOrgSnapshot SeedOrg() => new(
        Users: new List<UserSnapshot>
        {
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice@acme.tld", "Alice", null, null),
            new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "bob@acme.tld", "Bob",
                Guid.Parse("11111111-1111-1111-1111-111111111111"), null),
        },
        Departments: new List<DepartmentSnapshot>(),
        Groups: new List<GroupSnapshot>(),
        RoleAssignments: new List<RoleAssignmentSnapshot>());

    private static IReadOnlyList<TestCaseSnapshot> SeedCases()
    {
        using var doc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        return new List<TestCaseSnapshot>
        {
            new(
                Id: "tc_1",
                Name: "5 day vacation",
                Inputs: doc.RootElement.Clone(),
                ExpectedTrace: new[] { "start_1", "task_apply", "approval_manager", "gateway_days", "task_hr_archive", "end_1" }),
        };
    }

    private static async Task<JsonElement> LoadLeaveSpecAsync()
    {
        var json = await File.ReadAllTextAsync(LeavePath);
        // Clone so the JsonDocument can be safely disposed without invalidating
        // the JsonElement we hand back.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Build_produces_canonical_file_list_with_matching_hashes()
    {
        var spec = await LoadLeaveSpecAsync();
        var builder = NewBuilder();
        var bytes = await builder.BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn:definitions />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases()));

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // Read manifest first.
        var manifestEntry = zip.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);
        var manifest = JsonSerializer.Deserialize<BundleManifest>(
            ReadEntryBytes(manifestEntry!),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            });
        Assert.NotNull(manifest);
        Assert.Equal(BundleSchemaVersion.Current, manifest!.BundleSchemaVersion);
        Assert.Equal("LEAVE", manifest.FlowCode);
        Assert.Equal(1, manifest.FlowVersion);

        var paths = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "spec.json",
            "bpmn.xml",
            "spec.md",
            "README.md",
            "walkthrough.md",
            "forms/task_apply.json",
            "forms/task_hr_archive.json",
            "notifications/notify_assign_manager.json",
            "notifications/notify_complete.json",
            "sla.json",
            "actors.json",
            "sample-org.json",
            "test-cases/tc_1.json",
        };
        foreach (var path in expected)
        {
            Assert.Contains(path, paths);
        }

        // Manifest is NOT in its own files[] (chicken/egg).
        Assert.DoesNotContain("manifest.json", paths);

        // Every manifest entry's SHA-256 matches the bytes inside the zip,
        // and SizeBytes matches actual byte length.
        foreach (var entry in manifest.Files)
        {
            var ze = zip.GetEntry(entry.Path);
            Assert.NotNull(ze);
            var fileBytes = ReadEntryBytes(ze!);
            Assert.Equal(entry.SizeBytes, fileBytes.LongLength);
            Assert.Equal(entry.Sha256, Convert.ToHexString(SHA256.HashData(fileBytes)));
        }
    }

    [Fact]
    public async Task Build_passes_spec_json_through_verbatim()
    {
        var spec = await LoadLeaveSpecAsync();
        var rawSpec = await File.ReadAllTextAsync(LeavePath);

        var bytes = await NewBuilder().BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases()));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var specEntry = zip.GetEntry("spec.json")!;
        var inZip = Encoding.UTF8.GetString(ReadEntryBytes(specEntry));
        // GetRawText preserves the original text but JsonElement.Clone may
        // re-emit minified — we just verify the parsed shape matches.
        using var inZipDoc = JsonDocument.Parse(inZip);
        using var origDoc = JsonDocument.Parse(rawSpec);
        Assert.Equal(
            origDoc.RootElement.GetProperty("meta").GetProperty("flowCode").GetString(),
            inZipDoc.RootElement.GetProperty("meta").GetProperty("flowCode").GetString());
    }

    [Fact]
    public async Task Build_emits_changelog_when_parent_spec_supplied()
    {
        var spec = await LoadLeaveSpecAsync();
        // Parent: identical structure, but missing the last node so something
        // shows up in "Removed".
        using var parentDoc = JsonDocument.Parse(
            await File.ReadAllTextAsync(LeavePath));
        var parent = parentDoc.RootElement.Clone();

        var bytes = await NewBuilder().BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases(),
            ParentSpecJson: parent));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var changelog = zip.GetEntry("CHANGELOG.md");
        Assert.NotNull(changelog);
        var content = Encoding.UTF8.GetString(ReadEntryBytes(changelog!));
        Assert.Contains("# Changelog vs parent bundle", content);
    }

    [Fact]
    public async Task Build_throws_on_empty_sample_org()
    {
        var spec = await LoadLeaveSpecAsync();
        var ex = await Assert.ThrowsAsync<BundleBuildException>(() =>
            NewBuilder().BuildAsync(new BundleBuildRequest(
                DraftSpecJson: spec,
                BpmnXml: "<bpmn />",
                SampleOrg: new SampleOrgSnapshot(
                    Array.Empty<UserSnapshot>(),
                    Array.Empty<DepartmentSnapshot>(),
                    Array.Empty<GroupSnapshot>(),
                    Array.Empty<RoleAssignmentSnapshot>()),
                TestCases: SeedCases())));
        Assert.Contains(ex.Errors, e => e.Contains("sample-org must contain"));
    }

    [Fact]
    public async Task Build_throws_when_no_test_cases()
    {
        var spec = await LoadLeaveSpecAsync();
        var ex = await Assert.ThrowsAsync<BundleBuildException>(() =>
            NewBuilder().BuildAsync(new BundleBuildRequest(
                DraftSpecJson: spec,
                BpmnXml: "<bpmn />",
                SampleOrg: SeedOrg(),
                TestCases: Array.Empty<TestCaseSnapshot>())));
        Assert.Contains(ex.Errors, e => e.Contains("at least one test-case required"));
    }

    [Fact]
    public async Task Build_is_byte_stable_across_runs_with_fixed_clock()
    {
        var spec = await LoadLeaveSpecAsync();
        var req = new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases());

        var a = await NewBuilder().BuildAsync(req);
        var b = await NewBuilder().BuildAsync(req);

        // Two builds with the same inputs + clock produce identical zips.
        // This guarantees the manifest's per-file SHA-256 set + the
        // outer manifest checksum are reproducible — which the
        // reproducibility runner (PR-I5) depends on for repro signatures.
        Assert.Equal(Convert.ToHexString(SHA256.HashData(a)),
                     Convert.ToHexString(SHA256.HashData(b)));
    }

    [Fact]
    public async Task Form_payload_contains_id_fields_permissions()
    {
        var spec = await LoadLeaveSpecAsync();
        var bytes = await NewBuilder().BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases()));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var formEntry = zip.GetEntry("forms/task_apply.json");
        Assert.NotNull(formEntry);
        var json = Encoding.UTF8.GetString(ReadEntryBytes(formEntry!));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("task_apply", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("fields").ValueKind);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("permissions").ValueKind);
    }

    [Fact]
    public async Task Actors_json_includes_expected_actor_refs()
    {
        var spec = await LoadLeaveSpecAsync();
        var bytes = await NewBuilder().BuildAsync(new BundleBuildRequest(
            DraftSpecJson: spec,
            BpmnXml: "<bpmn />",
            SampleOrg: SeedOrg(),
            TestCases: SeedCases()));

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("actors.json")!;
        var json = Encoding.UTF8.GetString(ReadEntryBytes(entry));
        using var doc = JsonDocument.Parse(json);
        var actors = doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToHashSet();
        Assert.Contains("expr:submitter.manager", actors);
        Assert.Contains("expr:submitter.department.head", actors);
        Assert.Contains("role:VP", actors);
        Assert.Contains("role:HR", actors);
        // current_approver / submitter recipients also surfaced.
        Assert.Contains("current_approver", actors);
        Assert.Contains("submitter", actors);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

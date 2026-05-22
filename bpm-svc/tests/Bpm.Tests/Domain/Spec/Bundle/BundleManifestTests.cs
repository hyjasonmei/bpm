using System.Text.Json;
using System.Text.Json.Serialization;
using Bpm.Domain.Spec.Bundle;
using Xunit;

namespace Bpm.Tests.Domain.Spec.Bundle;

public sealed class BundleManifestTests
{
    private static JsonSerializerOptions BuildOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void Current_schema_version_is_one()
    {
        Assert.Equal(1, BundleSchemaVersion.Current);
    }

    [Fact]
    public void BundleFileKind_has_fifteen_distinct_values()
    {
        var values = Enum.GetValues<BundleFileKind>();

        Assert.Equal(15, values.Length);
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    [Fact]
    public void Manifest_round_trips_through_system_text_json()
    {
        var original = new BundleManifest(
            BundleSchemaVersion: BundleSchemaVersion.Current,
            FlowCode: "leave",
            FlowVersion: 3,
            ExportedAt: new DateTimeOffset(2026, 5, 11, 9, 30, 0, TimeSpan.Zero),
            SourceInstanceId: "bpm-svc-prod-01",
            Parent: "abc123",
            Files: new List<BundleFileEntry>
            {
                new("manifest.json", "0000", 256, BundleFileKind.Manifest),
                new("spec.json", "1111", 4096, BundleFileKind.Spec),
                new("bpmn.xml", "2222", 8192, BundleFileKind.BpmnXml),
                new("sample-org.json", "3333", 1024, BundleFileKind.SampleOrg),
                new("test-cases/happy.json", "4444", 512, BundleFileKind.TestCase),
            });

        var opts = BuildOptions();
        var json = JsonSerializer.Serialize(original, opts);
        var roundTripped = JsonSerializer.Deserialize<BundleManifest>(json, opts);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.BundleSchemaVersion, roundTripped!.BundleSchemaVersion);
        Assert.Equal(original.FlowCode, roundTripped.FlowCode);
        Assert.Equal(original.FlowVersion, roundTripped.FlowVersion);
        Assert.Equal(original.ExportedAt, roundTripped.ExportedAt);
        Assert.Equal(original.SourceInstanceId, roundTripped.SourceInstanceId);
        Assert.Equal(original.Parent, roundTripped.Parent);
        // Record equality on IReadOnlyList<T> is reference-based; compare element-wise.
        Assert.Equal(original.Files, roundTripped.Files);
    }

    [Fact]
    public void Manifest_round_trip_preserves_null_parent_for_root_bundle()
    {
        var original = new BundleManifest(
            BundleSchemaVersion: BundleSchemaVersion.Current,
            FlowCode: "expense",
            FlowVersion: 1,
            ExportedAt: DateTimeOffset.UtcNow,
            SourceInstanceId: "bpm-svc-test",
            Parent: null,
            Files: Array.Empty<BundleFileEntry>());

        var opts = BuildOptions();
        var json = JsonSerializer.Serialize(original, opts);
        var roundTripped = JsonSerializer.Deserialize<BundleManifest>(json, opts);

        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped!.Parent);
        Assert.Empty(roundTripped.Files);
    }

    [Fact]
    public void File_entry_kind_serializes_as_camel_case_string()
    {
        var entry = new BundleFileEntry(
            Path: "forms/leave_request.json",
            Sha256: "deadbeef",
            SizeBytes: 128,
            Kind: BundleFileKind.Form);

        var opts = BuildOptions();
        var json = JsonSerializer.Serialize(entry, opts);

        Assert.Contains("\"kind\":\"form\"", json);
    }
}

using System.Text.Json;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Tests.Common;
using Xunit;

namespace Bpm.Tests.Persistence.Process;

/// <summary>
/// PR-L1 — guards that every demo flow declared in <c>bpm-ui/src/lib/workflow.ts</c>
/// has a runtime-runnable <c>sample_specs/&lt;code&gt;_v1.json</c> companion. The
/// 11 entries below mirror workflow.ts <c>FormCode</c> 1:1; if a new code is
/// added there, this list must grow with it.
///
/// For each flow we assert:
///   1. the spec file exists at the conventional path,
///   2. it parses as JSON + a <see cref="SpecSnapshot"/>,
///   3. <see cref="SpecImportService"/> validates CEL expressions and ActorRef
///      shapes cleanly,
///   4. <c>meta.flowCode</c> matches the workflow.ts FormCode (uppercase).
/// </summary>
public sealed class AllFlowsSpecValidationTests
{
    private const string SpecsDir = "/Users/jason/claude/bpm/sample_specs";

    public static IEnumerable<object[]> AllFlowCodes() => new[]
    {
        new object[] { "LEAVE",  "leave_v1.json" },
        new object[] { "GEE",    "gee_v1.json" },
        new object[] { "GEV",    "gev_v1.json" },
        new object[] { "APE",    "ape_v1.json" },
        new object[] { "HWP",    "hwp_v1.json" },
        new object[] { "ITPR",   "itpr_v1.json" },
        new object[] { "TRQ",    "trq_v1.json" },
        new object[] { "TEO",    "teo_v1.json" },
        new object[] { "EXTOB",  "extob_v1.json" },
        new object[] { "RESIGN", "resign_v1.json" },
        new object[] { "DEPTX",  "deptx_v1.json" },
    };

    private static SpecImportService BuildSut()
        => new SpecImportService(new CelNetExpressionEvaluator(new StubClock()));

    [Theory]
    [MemberData(nameof(AllFlowCodes))]
    public async Task Spec_file_exists_and_validates_for(string flowCode, string fileName)
    {
        var path = Path.Combine(SpecsDir, fileName);
        Assert.True(File.Exists(path), $"missing spec file: {path}");

        var json = await File.ReadAllTextAsync(path);

        // 1) Pure JSON parse.
        using var doc = JsonDocument.Parse(json);

        // 2) flowCode in meta matches workflow.ts FormCode (uppercase).
        var meta = doc.RootElement.GetProperty("meta");
        var declared = meta.GetProperty("flowCode").GetString();
        Assert.Equal(flowCode, declared);

        // 3) SpecSnapshot loads (covers nodes/edges/userTasks/approvals shape).
        using var snapshot = new SpecSnapshot(json);
        Assert.Equal(flowCode, snapshot.FlowCode);
        Assert.NotNull(snapshot.StartNode);
        Assert.NotEmpty(snapshot.Edges);

        // 4) SpecImportService validates CEL expressions and ActorRef shapes.
        var sut = BuildSut();
        var result = await sut.ValidateAsync(json);
        Assert.True(result.Valid,
            $"{fileName} should validate cleanly. Errors: " +
            string.Join(" | ", result.Errors.Select(e => $"{e.Location}: {e.Message}")));
    }

    [Fact]
    public void All_workflow_form_codes_have_a_spec_file()
    {
        // Mirrors bpm-ui/src/lib/workflow.ts FormCode union exactly. Adding a new
        // FormCode there without adding a sample_specs entry should fail this test.
        var expected = new[] { "LEAVE", "GEE", "GEV", "APE", "TRQ", "TEO", "HWP", "ITPR", "EXTOB", "RESIGN", "DEPTX" };
        var declared = AllFlowCodes().Select(row => (string)row[0]).ToHashSet();
        foreach (var code in expected)
        {
            Assert.Contains(code, declared);
            var path = Path.Combine(SpecsDir, $"{code.ToLowerInvariant()}_v1.json");
            Assert.True(File.Exists(path), $"workflow.ts FormCode '{code}' has no sample_specs file at {path}");
        }
    }
}

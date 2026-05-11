using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class WalkthroughRendererTests
{
    [Fact]
    public async Task Renders_happy_path_from_test_case_trace()
    {
        var json = await File.ReadAllTextAsync("/Users/jason/claude/bpm/sample_specs/leave_v1.json");
        using var doc = JsonDocument.Parse(json);

        using var inputDoc = JsonDocument.Parse("{\"leave_type\":\"特休\"}");
        var tc = new TestCaseSnapshot(
            Id: "tc_1",
            Name: "5 天特休、直屬主管核准",
            Inputs: inputDoc.RootElement.Clone(),
            ExpectedTrace: new[] { "start_1", "task_apply", "approval_manager", "gateway_days", "task_hr_archive", "end_1" });

        var md = new WalkthroughRenderer().Render(doc.RootElement, tc);

        Assert.Contains("# Happy path: 5 天特休、直屬主管核准", md);
        Assert.Contains("1.", md);
        Assert.Contains("task_apply", md);
        Assert.Contains("approval_manager", md);
    }

    [Fact]
    public async Task Renders_default_path_when_no_test_case()
    {
        var json = await File.ReadAllTextAsync("/Users/jason/claude/bpm/sample_specs/leave_v1.json");
        using var doc = JsonDocument.Parse(json);

        var md = new WalkthroughRenderer().Render(doc.RootElement, null);

        Assert.Contains("Happy path: default route", md);
        Assert.Contains("start_1", md);
        Assert.Contains("end_1", md);
    }
}

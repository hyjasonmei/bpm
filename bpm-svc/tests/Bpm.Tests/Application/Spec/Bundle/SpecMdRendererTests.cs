using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class SpecMdRendererTests
{
    [Fact]
    public async Task Renders_leave_v1_with_all_sections_and_node_ids()
    {
        var json = await File.ReadAllTextAsync("/Users/jason/claude/bpm/sample_specs/leave_v1.json");
        using var doc = JsonDocument.Parse(json);
        var md = new SpecMdRenderer().Render(doc.RootElement);

        Assert.Contains("# 請假 (LEAVE v1)", md);
        Assert.Contains("## Flow nodes", md);
        Assert.Contains("## User tasks", md);
        Assert.Contains("## Decisions / Gateways", md);
        Assert.Contains("## Approvers", md);
        Assert.Contains("## Notifications", md);
        Assert.Contains("## SLA", md);
        Assert.Contains("## Actors used", md);

        // All seven node ids surface in the flow nodes list.
        var ids = new[] { "start_1", "task_apply", "approval_manager", "gateway_days", "approval_vp", "task_hr_archive", "end_1" };
        foreach (var id in ids)
        {
            Assert.Contains(id, md);
        }
    }
}

using System.Text.Json;
using Bpm.Application.Spec.Bundle;
using Xunit;

namespace Bpm.Tests.Application.Spec.Bundle;

public sealed class ChangelogRendererTests
{
    private const string Parent = """
    {
      "meta": { "flowCode": "X", "flowVersion": 1 },
      "flow": {
        "nodes": [
          { "id": "n1", "type": "startEvent" },
          { "id": "n2", "type": "userTask" }
        ],
        "edges": []
      },
      "userTasks": [
        { "id": "n2", "fields": [{"id":"a"}] }
      ],
      "notifications": [
        { "id": "old_notif", "trigger": "on_complete" }
      ]
    }
    """;

    private const string Child = """
    {
      "meta": { "flowCode": "X", "flowVersion": 2 },
      "flow": {
        "nodes": [
          { "id": "n1", "type": "startEvent" },
          { "id": "n2", "type": "userTask" },
          { "id": "n3", "type": "endEvent" }
        ],
        "edges": []
      },
      "userTasks": [
        { "id": "n2", "fields": [{"id":"a"},{"id":"b"}] }
      ],
      "notifications": []
    }
    """;

    [Fact]
    public void Diff_lists_added_removed_changed()
    {
        using var p = JsonDocument.Parse(Parent);
        using var c = JsonDocument.Parse(Child);

        var md = new ChangelogRenderer().Render(p.RootElement, c.RootElement);

        Assert.Contains("## Added", md);
        Assert.Contains("flow node n3", md);
        Assert.Contains("## Removed", md);
        Assert.Contains("notification old_notif", md);
        Assert.Contains("## Changed", md);
        Assert.Contains("userTask n2 form fields", md);
    }
}

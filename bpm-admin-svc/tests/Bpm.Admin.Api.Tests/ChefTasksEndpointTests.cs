using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bpm.Admin.Api.Tests.TestFixtures;
using Bpm.Admin.Domain.Flows;
using Bpm.Admin.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bpm.Admin.Api.Tests;

/// <summary>
/// PR-CA1: the chef agent's polling endpoint GET /api/chef/flows/tasks.
/// Each test spins its own factory (own in-memory DB) so the grouped
/// counts are deterministic.
/// </summary>
public class ChefTasksEndpointTests
{
    private sealed record TaskDto(Guid FlowId, string FlowCode, int Version, string DisplayName,
        FlowState State, DateTime UpdatedAt, string? ChefWorkContextJson, string? PrUrl, DateTime? LastUserMessageAt);
    private sealed record TaskListDto(List<TaskDto> Submitted, List<TaskDto> AwaitingChef,
        List<TaskDto> ApprovedAwaitingMerge, List<TaskDto> Stalled, List<TaskDto> Publishing);

    private sealed record PublishFailedRequest(string Reason);

    private static HttpClient ChefClient(AdminAppFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev-chef-token");
        return c;
    }

    private static Guid SeedFlow(AdminAppFactory f, string code, FlowState state,
        DateTime? mergedAt = null, DateTime? heartbeat = null)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var row = new Flow
        {
            Id = Guid.NewGuid(), LineageId = Guid.NewGuid(), Version = 1,
            State = state, FlowCode = code, DisplayName = code,
            MergedAt = mergedAt, LastChefHeartbeatAt = heartbeat,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Flows.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    private static void SeedMessage(AdminAppFactory f, Guid flowId, FlowChatSender sender, FlowChatKind kind)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        db.FlowChatMessages.Add(new FlowChatMessage
        {
            Id = Guid.NewGuid(), FlowId = flowId, Sender = sender, Kind = kind,
            Content = "x", CreatedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Submitted_flow_appears_in_Submitted()
    {
        using var f = new AdminAppFactory();
        SeedFlow(f, "PUR", FlowState.Submitted);
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.NotNull(tasks);
        Assert.Contains(tasks!.Submitted, t => t.FlowCode == "PUR");
    }

    [Fact]
    public async Task OnHold_with_last_user_reply_appears_in_AwaitingChef()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "LEAVE", FlowState.OnHold);
        SeedMessage(f, id, FlowChatSender.Chef, FlowChatKind.Question);
        SeedMessage(f, id, FlowChatSender.User, FlowChatKind.Reply);   // user replied last
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.Contains(tasks!.AwaitingChef, t => t.FlowCode == "LEAVE");
    }

    [Fact]
    public async Task OnHold_with_last_chef_question_is_not_AwaitingChef()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "LEAVE", FlowState.OnHold);
        SeedMessage(f, id, FlowChatSender.User, FlowChatKind.Reply);
        SeedMessage(f, id, FlowChatSender.Chef, FlowChatKind.Question);  // chef asked last
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.DoesNotContain(tasks!.AwaitingChef, t => t.FlowCode == "LEAVE");
    }

    [Fact]
    public async Task Approved_unmerged_appears_then_disappears_after_merge()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "APE", FlowState.Approved);
        var before = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.Contains(before!.ApprovedAwaitingMerge, t => t.FlowCode == "APE");

        var resp = await ChefClient(f).PostAsJsonAsync($"/api/chef/flows/{id}/merged", new { source = "test" });
        resp.EnsureSuccessStatusCode();

        var after = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.DoesNotContain(after!.ApprovedAwaitingMerge, t => t.FlowCode == "APE");
    }

    [Fact]
    public async Task Cooking_with_cold_heartbeat_appears_in_Stalled()
    {
        using var f = new AdminAppFactory();
        SeedFlow(f, "OLD", FlowState.Cooking, heartbeat: DateTime.UtcNow.AddMinutes(-35));
        SeedFlow(f, "HOT", FlowState.Cooking, heartbeat: DateTime.UtcNow.AddMinutes(-5));
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.Contains(tasks!.Stalled, t => t.FlowCode == "OLD");
        Assert.DoesNotContain(tasks!.Stalled, t => t.FlowCode == "HOT");
    }

    [Fact]
    public async Task Publishing_flow_appears_in_Publishing_group()
    {
        using var f = new AdminAppFactory();
        SeedFlow(f, "PUB", FlowState.Publishing);
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.NotNull(tasks);
        Assert.Contains(tasks!.Publishing, t => t.FlowCode == "PUB");
    }

    [Fact]
    public async Task MarkPublished_transitions_Publishing_to_Published()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "PUB", FlowState.Publishing);
        var resp = await ChefClient(f).PostAsync($"/api/chef/flows/{id}/published", null);
        resp.EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var row = await db.Flows.FindAsync(id);
        Assert.Equal(FlowState.Published, row!.State);
    }

    [Fact]
    public async Task MarkPublishFailed_transitions_Publishing_to_PublishFailed_with_reason()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "PUB", FlowState.Publishing);
        var resp = await ChefClient(f).PostAsJsonAsync($"/api/chef/flows/{id}/publish-failed",
            new PublishFailedRequest("az deploy timed out"));
        resp.EnsureSuccessStatusCode();

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AdminDbContext>();
        var row = await db.Flows.FindAsync(id);
        Assert.Equal(FlowState.PublishFailed, row!.State);
        Assert.Equal("az deploy timed out", row.PublishFailedReason);
    }

    [Fact]
    public async Task Published_endpoint_rejects_without_chef_token()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "PUB", FlowState.Publishing);
        // No Authorization header → FallbackPolicy 401 (or 403); not a success.
        var resp = await f.CreateClient().PostAsync($"/api/chef/flows/{id}/published", null);
        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task PublishFailed_endpoint_rejects_without_chef_token()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "PUB", FlowState.Publishing);
        var resp = await f.CreateClient().PostAsJsonAsync($"/api/chef/flows/{id}/publish-failed",
            new PublishFailedRequest("nope"));
        Assert.False(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SetPr_then_tasks_carries_prUrl()
    {
        using var f = new AdminAppFactory();
        var id = SeedFlow(f, "APE", FlowState.Approved);
        var resp = await ChefClient(f).PostAsJsonAsync($"/api/chef/flows/{id}/pr",
            new { prUrl = "https://github.com/x/y/pull/9" });
        resp.EnsureSuccessStatusCode();
        var tasks = await ChefClient(f).GetFromJsonAsync<TaskListDto>("/api/chef/flows/tasks");
        Assert.Equal("https://github.com/x/y/pull/9",
            tasks!.ApprovedAwaitingMerge.Single(t => t.FlowCode == "APE").PrUrl);
    }
}

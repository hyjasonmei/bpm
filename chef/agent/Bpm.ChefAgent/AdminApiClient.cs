using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Bpm.ChefAgent;

/// <summary>
/// Thin HttpClient wrapper for one environment's chef-token API surface.
/// All calls carry the env's chef bearer; a short timeout keeps an
/// unreachable (e.g. stopped Azure) environment from stalling the whole poll.
/// </summary>
public sealed class AdminApiClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public AdminApiClient(EnvTarget env, HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.BaseAddress = new Uri(env.BaseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", env.ChefToken);
    }

    /// <summary>GET the grouped work queue. Throws on transport/HTTP error so
    /// the caller can record an env failure + back off.</summary>
    public async Task<ChefTaskList> GetTasksAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/chef/flows/tasks", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ChefTaskList>(Json, ct) ?? ChefTaskList.Empty();
    }

    /// <summary>Claim a Submitted flow (Submitted→Cooking). Returns false on
    /// 409 (someone else already moved it), true on success.</summary>
    public async Task<bool> TryClaimAsync(Guid flowId, CancellationToken ct = default)
    {
        var resp = await PostTransitionAsync(flowId, "Cooking", ct: ct);
        if (resp.StatusCode == HttpStatusCode.Conflict) return false;
        resp.EnsureSuccessStatusCode();
        return true;
    }

    public async Task SetPrUrlAsync(Guid flowId, string prUrl, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/chef/flows/{flowId}/pr", new { prUrl }, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task MarkMergedAsync(Guid flowId, string source, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/chef/flows/{flowId}/merged", new { source }, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Move a stuck Cooking flow to OnHold with an auto-question, so a
    /// give-up leaves the cook queue and surfaces to a human (PR-CA1).</summary>
    public async Task OnHoldAsync(Guid flowId, string question, CancellationToken ct = default)
    {
        var resp = await PostTransitionAsync(flowId, "OnHold", question, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task PostMemoAsync(Guid flowId, string content, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/chef/flows/{flowId}/messages",
            new { kind = "Memo", content }, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Deploy succeeded → Publishing→Published (Task 6).</summary>
    public async Task MarkPublishedAsync(Guid flowId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"api/chef/flows/{flowId}/published", content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Deploy failed → Publishing→PublishFailed with a reason (Task 6).</summary>
    public async Task MarkPublishFailedAsync(Guid flowId, string reason, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"api/chef/flows/{flowId}/publish-failed", new { reason }, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Per-env Azure resource NAMES (no secrets) for the deploy worker.</summary>
    public async Task<List<DeployEnvConfig>> GetDeployConfigAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("api/chef/flows/deploy-config", ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<DeployEnvConfig>>(Json, ct) ?? [];
    }

    /// <summary>Fetch the current state of one flow (post-cook outcome check).</summary>
    public async Task<string?> GetStateAsync(Guid flowId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"api/chef/flows/{flowId}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;   // flow deleted mid-cook
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("state", out var s) ? s.GetString() : null;
    }

    private Task<HttpResponseMessage> PostTransitionAsync(Guid flowId, string target, string? question = null, CancellationToken ct = default)
        => _http.PostAsJsonAsync($"api/chef/flows/{flowId}/transition", new { target, question }, ct);

    public void Dispose() => _http.Dispose();
}

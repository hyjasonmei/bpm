using System.Net;
using System.Net.Http.Json;
using Bpm.Admin.Api.Tests.TestFixtures;
using Xunit;

namespace Bpm.Admin.Api.Tests;

/// <summary>
/// Task 5: GET / PUT /api/site-setting/deploy-config. Admin-JWT gated via
/// the global FallbackPolicy (same as the other admin write endpoints).
/// </summary>
public class DeployConfigEndpointTests
{
    private sealed record EnvCfgDto(string EnvName, string ResourceGroup, string BpmSvcApp,
        string AdminSvcApp, string BpmUiSwa, string AdminUiSwa, bool Enabled);

    private sealed record UpsertReq(string EnvName, string ResourceGroup, string BpmSvcApp,
        string AdminSvcApp, string BpmUiSwa, string AdminUiSwa, bool Enabled);

    [Fact]
    public async Task Put_then_Get_round_trips()
    {
        using var f = new AdminAppFactory();
        var client = f.CreateAdminClient();

        var put = await client.PutAsJsonAsync("/api/site-setting/deploy-config",
            new UpsertReq("azure-poc", "rg-poc", "poc-flowcook-api", "poc-flowcook-admin-api",
                "poc-flowcook-ui", "poc-flowcook-admin-ui", true));
        put.EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<List<EnvCfgDto>>("/api/site-setting/deploy-config");
        var cfg = Assert.Single(list!);
        Assert.Equal("azure-poc", cfg.EnvName);
        Assert.Equal("poc-flowcook-admin-api", cfg.AdminSvcApp);
        Assert.True(cfg.Enabled);
    }

    [Fact]
    public async Task Get_rejects_without_admin_token()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().GetAsync("/api/site-setting/deploy-config");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Put_rejects_without_admin_token()
    {
        using var f = new AdminAppFactory();
        var resp = await f.CreateClient().PutAsJsonAsync("/api/site-setting/deploy-config",
            new UpsertReq("azure-poc", "rg", "a", "b", "c", "d", true));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

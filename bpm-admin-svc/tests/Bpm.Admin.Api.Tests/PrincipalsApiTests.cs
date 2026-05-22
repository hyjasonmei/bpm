using System.Net;
using System.Net.Http.Json;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Api.Tests.TestFixtures;
using Xunit;

namespace Bpm.Admin.Api.Tests;

public class PrincipalsApiTests : IClassFixture<AdminAppFactory>
{
    private readonly AdminAppFactory _factory;

    public PrincipalsApiTests(AdminAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Empty_list_returns_OK_and_empty_array()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/principals");
        resp.EnsureSuccessStatusCode();
        var items = await resp.Content.ReadFromJsonAsync<List<PrincipalDto>>();
        Assert.NotNull(items);
    }

    [Fact]
    public async Task Full_CRUD_lifecycle()
    {
        var client = _factory.CreateClient();

        var createResp = await client.PostAsJsonAsync("/api/principals", new CreatePrincipalRequest(
            PrincipalType.User, "Alice Test", "alice@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await createResp.Content.ReadFromJsonAsync<PrincipalDto>();
        Assert.NotNull(created);
        Assert.Equal("Alice Test", created!.DisplayName);

        var getResp = await client.GetAsync($"/api/principals/{created.Id}");
        getResp.EnsureSuccessStatusCode();

        var updateResp = await client.PutAsJsonAsync($"/api/principals/{created.Id}",
            new UpdatePrincipalRequest("Alice Updated", null, null));
        updateResp.EnsureSuccessStatusCode();
        var updated = await updateResp.Content.ReadFromJsonAsync<PrincipalDto>();
        Assert.Equal("Alice Updated", updated!.DisplayName);

        var deleteResp = await client.DeleteAsync($"/api/principals/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var getAfter = await client.GetAsync($"/api/principals/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);
    }

    [Fact]
    public async Task Filter_by_type()
    {
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/principals", new CreatePrincipalRequest(PrincipalType.User, "U1", null));
        await client.PostAsJsonAsync("/api/principals", new CreatePrincipalRequest(PrincipalType.Dept, "D1", null));
        await client.PostAsJsonAsync("/api/principals", new CreatePrincipalRequest(PrincipalType.Group, "G1", null));

        var users = await client.GetFromJsonAsync<List<PrincipalDto>>("/api/principals?type=User");
        Assert.NotNull(users);
        Assert.Contains(users!, p => p.DisplayName == "U1");
        Assert.DoesNotContain(users!, p => p.DisplayName == "D1");
    }
}

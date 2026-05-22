using System.Net;
using System.Net.Http.Json;
using Bpm.Admin.Api.Controllers;
using Bpm.Admin.Application.Auth;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Api.Tests.TestFixtures;
using Xunit;

namespace Bpm.Admin.Api.Tests;

public class AuthApiTests : IClassFixture<AdminAppFactory>
{
    private readonly AdminAppFactory _factory;

    public AuthApiTests(AdminAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Full_Login_Logout_Flow()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });

        var createResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "AuthFlowUser", "auth-flow@example.com"));
        var user = await createResp.Content.ReadFromJsonAsync<PrincipalDto>();
        Assert.NotNull(user);

        var pwResp = await client.PutAsJsonAsync($"/api/principals/{user!.Id}/password", new SetPasswordRequest("hunter22"));
        Assert.Equal(HttpStatusCode.NoContent, pwResp.StatusCode);

        // Wrong password
        var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("auth-flow@example.com", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        // Right password
        var ok = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("auth-flow@example.com", "hunter22"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var loginBody = await ok.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(user.Id, loginBody!.UserId);

        // Cookie is now in client; /me should succeed
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(user.Id, meBody!.UserId);

        // Logout
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // /me should now 401
        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task Login_Without_Credential_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var createResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "NoCredUser", "no-cred@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("no-cred@example.com", "whatever"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Setting_Password_On_Non_User_Principal_Fails()
    {
        var client = _factory.CreateClient();
        var dResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.Dept, "PwTestDept", null));
        var dept = await dResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var pwResp = await client.PutAsJsonAsync($"/api/principals/{dept!.Id}/password", new SetPasswordRequest("hunter22"));
        Assert.Equal(HttpStatusCode.BadRequest, pwResp.StatusCode);
    }
}

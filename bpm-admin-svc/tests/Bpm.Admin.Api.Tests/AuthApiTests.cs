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
    public async Task Login_Mints_Jwt_That_Authorizes_Me()
    {
        // Setup (principal + password) hits gated endpoints → needs an admin
        // bearer. The auth assertions below run on `client` (no default token) so
        // the "no bearer → 401" check is meaningful.
        var admin = _factory.CreateAdminClient();
        var client = _factory.CreateClient();

        var createResp = await admin.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "AuthFlowUser", "auth-flow@example.com"));
        var user = await createResp.Content.ReadFromJsonAsync<PrincipalDto>();
        Assert.NotNull(user);

        var pwResp = await admin.PutAsJsonAsync($"/api/principals/{user!.Id}/password", new SetPasswordRequest("hunter22"));
        Assert.Equal(HttpStatusCode.NoContent, pwResp.StatusCode);

        // Wrong password
        var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("auth-flow@example.com", "wrong"));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        // Right password → JWT in the body (no cookie)
        var ok = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("auth-flow@example.com", "hunter22"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var loginBody = await ok.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(user.Id, loginBody!.UserId);
        Assert.False(string.IsNullOrWhiteSpace(loginBody.Token));

        // Bearer the token → /me succeeds and echoes the user
        var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody.Token);
        var me = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = await me.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal(user.Id, meBody!.UserId);

        // No bearer → /me is anonymous → 401
        var meAnon = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAnon.StatusCode);

        // Logout is a stateless no-op (token TTL governs)
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Login_Without_Credential_Returns_Unauthorized()
    {
        var admin = _factory.CreateAdminClient();
        var client = _factory.CreateClient();
        var createResp = await admin.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "NoCredUser", "no-cred@example.com"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("no-cred@example.com", "whatever"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Setting_Password_On_Non_User_Principal_Fails()
    {
        var client = _factory.CreateAdminClient();
        var dResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.Dept, "PwTestDept", null));
        var dept = await dResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var pwResp = await client.PutAsJsonAsync($"/api/principals/{dept!.Id}/password", new SetPasswordRequest("hunter22"));
        Assert.Equal(HttpStatusCode.BadRequest, pwResp.StatusCode);
    }
}

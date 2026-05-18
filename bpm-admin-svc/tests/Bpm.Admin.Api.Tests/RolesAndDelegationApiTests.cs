using System.Net;
using System.Net.Http.Json;
using Bpm.Admin.Application.Delegations;
using Bpm.Admin.Application.Principals;
using Bpm.Admin.Application.Roles;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Api.Tests.TestFixtures;
using Xunit;

namespace Bpm.Admin.Api.Tests;

public class RolesAndDelegationApiTests : IClassFixture<AdminAppFactory>
{
    private readonly AdminAppFactory _factory;

    public RolesAndDelegationApiTests(AdminAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Role_CRUD_and_assignment()
    {
        var client = _factory.CreateClient();

        var roleResp = await client.PostAsJsonAsync("/api/roles",
            new CreateRoleRequest("Approver_" + Guid.NewGuid().ToString("N").Substring(0, 6), null));
        Assert.Equal(HttpStatusCode.Created, roleResp.StatusCode);
        var role = await roleResp.Content.ReadFromJsonAsync<RoleDto>();
        Assert.NotNull(role);

        var listResp = await client.GetFromJsonAsync<List<RoleDto>>("/api/roles");
        Assert.Contains(listResp!, r => r.Id == role!.Id);

        var userResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "RoleTestUser", null));
        var user = await userResp.Content.ReadFromJsonAsync<PrincipalDto>();
        Assert.NotNull(user);

        var assignResp = await client.PostAsJsonAsync($"/api/principals/{user!.Id}/roles",
            new AssignRoleRequest(role!.Id, InheritToMembers: false));
        Assert.Equal(HttpStatusCode.NoContent, assignResp.StatusCode);

        var effectiveResp = await client.GetFromJsonAsync<List<EffectiveRole>>($"/api/principals/{user.Id}/effective-roles");
        Assert.NotNull(effectiveResp);
        Assert.Contains(effectiveResp!, e => e.RoleId == role.Id && e.SourcePrincipalId == user.Id);

        var revokeResp = await client.DeleteAsync($"/api/principals/{user.Id}/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revokeResp.StatusCode);
    }

    [Fact]
    public async Task Delegation_Lifecycle_and_Active_Filter()
    {
        var client = _factory.CreateClient();

        var dResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "Delegator", null));
        var delegator = await dResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var tResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "Delegate", null));
        var delegate_ = await tResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var createResp = await client.PostAsJsonAsync("/api/delegations", new CreateDelegationRequest(
            delegator!.Id, delegate_!.Id, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddDays(5), "vacation"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var d = await createResp.Content.ReadFromJsonAsync<DelegationDto>();

        var activeNow = await client.GetFromJsonAsync<List<DelegationDto>>(
            $"/api/delegations?delegatorPrincipalId={delegator.Id}&onlyActive=true");
        Assert.NotNull(activeNow);
        Assert.Contains(activeNow!, x => x.Id == d!.Id);

        var cancelResp = await client.DeleteAsync($"/api/delegations/{d!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);

        var afterCancel = await client.GetFromJsonAsync<List<DelegationDto>>(
            $"/api/delegations?delegatorPrincipalId={delegator.Id}&onlyActive=true");
        Assert.DoesNotContain(afterCancel!, x => x.Id == d.Id);
    }

    [Fact]
    public async Task Delegation_target_must_be_user()
    {
        var client = _factory.CreateClient();

        var delegatorResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.User, "DelTargetBadDel", null));
        var delegator = await delegatorResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var deptResp = await client.PostAsJsonAsync("/api/principals",
            new CreatePrincipalRequest(PrincipalType.Dept, "ATargetDept", null));
        var dept = await deptResp.Content.ReadFromJsonAsync<PrincipalDto>();

        var resp = await client.PostAsJsonAsync("/api/delegations", new CreateDelegationRequest(
            delegator!.Id, dept!.Id, DateTime.UtcNow, DateTime.UtcNow.AddDays(1), null));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

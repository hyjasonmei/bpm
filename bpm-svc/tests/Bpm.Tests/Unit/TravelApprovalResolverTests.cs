using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;
using Bpm.Application.Travel.Services;
using Bpm.Tests.Common;
using FluentAssertions;

namespace Bpm.Tests.Unit;

public class TravelApprovalResolverTests
{
    [Fact]
    public async Task DirectManager_returns_applicants_manager()
    {
        var resolver = new TravelApprovalResolver(TestEmployees.Default());
        var id = await resolver.ResolveManagerApproverAsync("u_wilson");
        id.Should().Be("u_wang_manager");
    }

    [Fact]
    public async Task Vp_resolves_via_department_head()
    {
        var resolver = new TravelApprovalResolver(TestEmployees.Default());
        var id = await resolver.ResolveVpApproverAsync("u_wilson");
        id.Should().Be("u_chen_vp");
    }

    [Fact]
    public async Task Vp_falls_back_to_role_when_no_dept_head()
    {
        var employees = new List<Employee>
        {
            TestEmployees.Wilson,
            TestEmployees.WangManager,
            TestEmployees.ChenVp with { Roles = new[] { "VP" } },  // no DEPT_HEAD
        };
        var resolver = new TravelApprovalResolver(new FakeIdentityProvider(employees));
        var id = await resolver.ResolveVpApproverAsync("u_wilson");
        id.Should().Be("u_chen_vp");
    }

    [Fact]
    public async Task Vp_throws_when_no_match()
    {
        var employees = new List<Employee>
        {
            TestEmployees.Wilson,
            TestEmployees.WangManager,
            TestEmployees.ChenVp with { Roles = Array.Empty<string>() },
        };
        var resolver = new TravelApprovalResolver(new FakeIdentityProvider(employees));
        var act = () => resolver.ResolveVpApproverAsync("u_wilson");
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

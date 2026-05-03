using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;
using Bpm.Application.Purchase.Services;
using Bpm.Tests.Common;
using FluentAssertions;

namespace Bpm.Tests.Unit;

public class PurchaseApprovalResolverTests
{
    [Fact]
    public async Task DirectManager_returns_applicants_manager_id()
    {
        // spec.approvals[approval_manager].rule.type = "direct_manager"
        var resolver = new PurchaseApprovalResolver(TestEmployees.Default());

        var managerId = await resolver.ResolveManagerApproverAsync("u_wilson");

        managerId.Should().Be("u_wang_manager");
    }

    [Fact]
    public async Task FinanceApprover_resolves_via_role_Finance()
    {
        // spec.approvals[approval_finance].rule = { type: role, role: Finance }
        var resolver = new PurchaseApprovalResolver(TestEmployees.Default());

        var financeId = await resolver.ResolveFinanceApproverAsync();

        financeId.Should().Be("u_finance_lead");
    }

    [Fact]
    public async Task CeoApprover_resolves_via_role_CEO()
    {
        // spec.approvals[approval_ceo].rule = { type: role, role: CEO }
        var resolver = new PurchaseApprovalResolver(TestEmployees.Default());

        var ceoId = await resolver.ResolveCeoApproverAsync();

        ceoId.Should().Be("u_ceo");
    }

    [Fact]
    public async Task CeoApprover_falls_back_to_role_VP_when_CEO_missing()
    {
        // spec.approvals[approval_ceo].fallback = { type: role, role: VP }
        var employees = new List<Employee>
        {
            TestEmployees.Wilson,
            TestEmployees.WangManager,
            TestEmployees.ChenVp,        // has VP role
            TestEmployees.FinanceLead,
        };
        var resolver = new PurchaseApprovalResolver(new FakeIdentityProvider(employees));

        var ceoId = await resolver.ResolveCeoApproverAsync();

        ceoId.Should().Be("u_chen_vp");
    }

    [Fact]
    public async Task CeoApprover_throws_when_neither_CEO_nor_VP_exists()
    {
        var employees = new List<Employee>
        {
            TestEmployees.Wilson,
            TestEmployees.WangManager,
            TestEmployees.ChenVp with { Roles = Array.Empty<string>() },
        };
        var resolver = new PurchaseApprovalResolver(new FakeIdentityProvider(employees));

        var act = () => resolver.ResolveCeoApproverAsync();

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ManagerApprover_throws_when_applicant_has_no_manager()
    {
        var employees = new List<Employee> { TestEmployees.Ceo };  // Ceo.ManagerId == null
        var resolver = new PurchaseApprovalResolver(new FakeIdentityProvider(employees));

        var act = () => resolver.ResolveManagerApproverAsync("u_ceo");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task FinanceApprover_throws_when_no_Finance_role_holder()
    {
        var employees = new List<Employee> { TestEmployees.Wilson, TestEmployees.WangManager };
        var resolver = new PurchaseApprovalResolver(new FakeIdentityProvider(employees));

        var act = () => resolver.ResolveFinanceApproverAsync();

        await act.Should().ThrowAsync<NotFoundException>();
    }
}

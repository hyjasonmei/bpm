using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;

namespace Bpm.Application.Purchase.Services;

/// Implements spec.approvals[]:
///   approval_manager: { type: direct_manager }
///   approval_finance: { type: role, role: Finance }
///   approval_ceo:     { type: role, role: CEO }, fallback { type: role, role: VP }
public sealed class PurchaseApprovalResolver(IIdentityProvider identity)
{
    public async Task<string> ResolveManagerApproverAsync(string applicantUserId, CancellationToken ct = default)
    {
        var applicant = await identity.FindByIdAsync(applicantUserId, ct)
            ?? throw new NotFoundException("Employee", applicantUserId);

        if (string.IsNullOrEmpty(applicant.ManagerId))
            throw new NotFoundException($"Direct manager not found for applicant '{applicantUserId}'.");

        return applicant.ManagerId;
    }

    public async Task<string> ResolveFinanceApproverAsync(CancellationToken ct = default)
    {
        var finance = await identity.FindByRoleAsync("Finance", ct)
            ?? throw new NotFoundException("Finance approver not found via role 'Finance'.");
        return finance.EmployeeId;
    }

    public async Task<string> ResolveCeoApproverAsync(CancellationToken ct = default)
    {
        var ceo = await identity.FindByRoleAsync("CEO", ct);
        if (ceo is not null) return ceo.EmployeeId;

        var vp = await identity.FindByRoleAsync("VP", ct);
        if (vp is not null) return vp.EmployeeId;

        throw new NotFoundException("CEO approver not found via role 'CEO' or fallback role 'VP'.");
    }
}

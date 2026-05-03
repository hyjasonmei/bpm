using Bpm.Application.Common.Exceptions;
using Bpm.Application.Common.Identity;

namespace Bpm.Application.Travel.Services;

/// Implements spec.approvals[]:
///   approval_manager: { type: direct_manager }
///   approval_vp:      { type: department_head, deptOf: applicant }, fallback { type: role, role: VP }
public sealed class TravelApprovalResolver(IIdentityProvider identity)
{
    public async Task<string> ResolveManagerApproverAsync(string applicantUserId, CancellationToken ct = default)
    {
        var applicant = await identity.FindByIdAsync(applicantUserId, ct)
            ?? throw new NotFoundException("Employee", applicantUserId);

        if (string.IsNullOrEmpty(applicant.ManagerId))
            throw new NotFoundException($"Direct manager not found for applicant '{applicantUserId}'.");

        return applicant.ManagerId;
    }

    public async Task<string> ResolveVpApproverAsync(string applicantUserId, CancellationToken ct = default)
    {
        var applicant = await identity.FindByIdAsync(applicantUserId, ct)
            ?? throw new NotFoundException("Employee", applicantUserId);

        var head = await identity.FindDepartmentHeadAsync(applicant.Department, ct);
        if (head is not null) return head.EmployeeId;

        var vp = await identity.FindByRoleAsync("VP", ct);
        if (vp is not null) return vp.EmployeeId;

        throw new NotFoundException(
            $"VP approver not found via department_head(dept={applicant.Department}) or fallback role 'VP'.");
    }
}

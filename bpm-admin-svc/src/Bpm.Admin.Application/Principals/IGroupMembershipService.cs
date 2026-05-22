using Bpm.Admin.Domain.Principals;

namespace Bpm.Admin.Application.Principals;

public interface IGroupMembershipService
{
    Task AddMemberAsync(Guid groupId, Guid memberPrincipalId, PrincipalType memberType, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid groupId, Guid memberPrincipalId, CancellationToken ct = default);
}

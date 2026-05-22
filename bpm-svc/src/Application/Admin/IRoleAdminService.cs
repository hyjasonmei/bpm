using Bpm.Application.Admin.Dtos;

namespace Bpm.Application.Admin;

public interface IRoleAdminService
{
    Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct = default);
    Task<PagedResult<UserSummaryDto>> ListUsersAsync(string? q, int page, int pageSize, string? roleCode, CancellationToken ct = default);
    Task<UserDetailDto> GetUserDetailAsync(Guid userId, CancellationToken ct = default);
    Task<AssignmentDto> AssignRoleAsync(Guid actorUserId, Guid targetUserId, AssignRoleRequest req, CancellationToken ct = default);
    Task RevokeAssignmentAsync(Guid actorUserId, Guid targetUserId, Guid assignmentId, CancellationToken ct = default);
}

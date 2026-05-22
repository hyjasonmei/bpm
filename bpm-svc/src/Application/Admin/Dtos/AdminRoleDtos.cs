using Bpm.Domain.Entities.Authz;

namespace Bpm.Application.Admin.Dtos;

public sealed record RoleSummaryDto(Guid Id, string Code, string Name, RoleScope Scope, int AssignedCount);

public sealed record UserSummaryDto(
    Guid Id,
    string FullName,
    string Email,
    string? DepartmentCode,
    bool IsActive,
    int RoleCount);

public sealed record AssignmentDto(
    Guid Id,
    Guid RoleId,
    string RoleCode,
    string RoleName,
    AssignmentScope Scope,
    string? ScopeRef,
    DateTime AssignedAt,
    Guid? AssignedBy);

public sealed record UserDetailDto(
    UserSummaryDto Profile,
    IReadOnlyList<AssignmentDto> Assignments);

public sealed record AssignRoleRequest(string RoleCode, AssignmentScope? Scope, string? ScopeRef);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

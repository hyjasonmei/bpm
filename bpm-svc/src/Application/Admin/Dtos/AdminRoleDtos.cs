namespace Bpm.Application.Admin.Dtos;

// After unify-user-store: admin's Admin_Roles table is the canonical role
// store. It has Id + Name (no Code, no Scope, no FlowCode columns), so the
// DTOs here flatten to Name-as-identifier. Frontend callers that previously
// passed `roleCode` now pass `roleName`.

public sealed record RoleSummaryDto(Guid Id, string Name, bool IsSystem, int AssignedCount);

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
    string RoleName,
    DateTime AssignedAt,
    Guid? AssignedBy);

public sealed record UserDetailDto(
    UserSummaryDto Profile,
    IReadOnlyList<AssignmentDto> Assignments);

public sealed record AssignRoleRequest(string RoleName);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

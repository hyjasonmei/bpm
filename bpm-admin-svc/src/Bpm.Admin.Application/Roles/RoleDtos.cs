namespace Bpm.Admin.Application.Roles;

public record RoleDto(Guid Id, string Code, string Name, bool IsSystem, string? Description);

public record CreateRoleRequest(string Code, string Name, string? Description, bool IsSystem = false);

public record UpdateRoleRequest(string Name, string? Description);

public record AssignRoleRequest(Guid RoleId, bool InheritToMembers);

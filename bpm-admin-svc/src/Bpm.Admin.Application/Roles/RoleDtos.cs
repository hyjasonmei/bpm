namespace Bpm.Admin.Application.Roles;

public record RoleDto(Guid Id, string Name, bool IsSystem, string? Description);

public record CreateRoleRequest(string Name, string? Description, bool IsSystem = false);

public record UpdateRoleRequest(string Name, string? Description);

public record AssignRoleRequest(Guid RoleId, bool InheritToMembers);

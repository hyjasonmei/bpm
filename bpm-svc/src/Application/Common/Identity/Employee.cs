namespace Bpm.Application.Common.Identity;

public sealed record Employee(
    string EmployeeId,
    string DisplayName,
    string Email,
    string? ManagerId,
    string Department,
    string Title,
    IReadOnlyCollection<string> Roles
);

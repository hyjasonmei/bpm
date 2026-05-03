namespace Bpm.Application.Common.Identity;

public interface IIdentityProvider
{
    Task<Employee?> FindByIdAsync(string employeeId, CancellationToken ct = default);

    Task<Employee?> FindDepartmentHeadAsync(string department, CancellationToken ct = default);

    Task<Employee?> FindByRoleAsync(string role, CancellationToken ct = default);
}

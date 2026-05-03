using Bpm.Application.Common.Identity;

namespace Bpm.Tests.Common;

internal sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly Dictionary<string, Employee> _byId;

    public FakeIdentityProvider(IEnumerable<Employee> employees)
        => _byId = employees.ToDictionary(e => e.EmployeeId, StringComparer.Ordinal);

    public Task<Employee?> FindByIdAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var e) ? e : null);

    public Task<Employee?> FindDepartmentHeadAsync(string department, CancellationToken ct = default) =>
        Task.FromResult(_byId.Values.FirstOrDefault(e => e.Department == department && e.Roles.Contains("DEPT_HEAD")));

    public Task<Employee?> FindByRoleAsync(string role, CancellationToken ct = default) =>
        Task.FromResult(_byId.Values.FirstOrDefault(e => e.Roles.Contains(role)));
}

internal static class TestEmployees
{
    public static readonly Employee Wilson      = new("u_wilson",       "Wilson Liu",   "wilson@acme.example", "u_wang_manager", "Engineering", "Senior Engineer",  Array.Empty<string>());
    public static readonly Employee WangManager = new("u_wang_manager", "Wang Manager", "wang@acme.example",   "u_chen_vp",      "Engineering", "Eng Manager",      Array.Empty<string>());
    public static readonly Employee ChenVp      = new("u_chen_vp",      "Chen VP",      "chen@acme.example",   null,             "Engineering", "VP Engineering",   new[] { "DEPT_HEAD", "VP" });
    public static readonly Employee AdminLead   = new("u_admin_lead",   "Anna Admin",   "admin@acme.example",  "u_chen_vp",      "Operations",  "Admin Lead",       new[] { "Admin" });

    public static FakeIdentityProvider Default() => new(new[] { Wilson, WangManager, ChenVp, AdminLead });
}

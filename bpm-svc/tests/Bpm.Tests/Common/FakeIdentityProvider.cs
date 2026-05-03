using Bpm.Application.Common.Identity;

namespace Bpm.Tests.Common;

internal sealed class FakeIdentityProvider : IIdentityProvider
{
    private readonly Dictionary<string, Employee> _byId;

    public FakeIdentityProvider(IEnumerable<Employee> employees)
    {
        _byId = employees.ToDictionary(e => e.EmployeeId, StringComparer.Ordinal);
    }

    public Task<Employee?> FindByIdAsync(string employeeId, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(employeeId, out var e) ? e : null);

    public Task<Employee?> FindDepartmentHeadAsync(string department, CancellationToken ct = default)
    {
        var head = _byId.Values.FirstOrDefault(e => e.Department == department && e.Roles.Contains("DEPT_HEAD"));
        return Task.FromResult(head);
    }

    public Task<Employee?> FindByRoleAsync(string role, CancellationToken ct = default)
    {
        var match = _byId.Values.FirstOrDefault(e => e.Roles.Contains(role));
        return Task.FromResult(match);
    }
}

internal static class TestEmployees
{
    // Mirrors bpm-svc/src/Api/identity-acme.csv for spec.testCases:
    public static readonly Employee Wilson        = new("u_wilson",        "Wilson Liu",     "wilson@acme.example",   "u_wang_manager",  "Engineering", "Senior Engineer",   Array.Empty<string>());
    public static readonly Employee WangManager   = new("u_wang_manager",  "Wang Manager",   "wang@acme.example",     "u_chen_vp",       "Engineering", "Eng Manager",       Array.Empty<string>());
    public static readonly Employee ChenVp        = new("u_chen_vp",       "Chen VP",        "chen@acme.example",     "u_ceo",           "Engineering", "VP Engineering",    new[] { "DEPT_HEAD", "VP" });
    public static readonly Employee FinanceLead   = new("u_finance_lead",  "Lin Finance",    "finance@acme.example",  "u_ceo",           "Finance",     "Finance Director",  new[] { "Finance", "DEPT_HEAD" });
    public static readonly Employee PurchaseLead  = new("u_purchase_lead", "Sam Purchasing", "purchase@acme.example", "u_finance_lead",  "Finance",     "Purchasing Lead",   new[] { "Purchase" });
    public static readonly Employee Ceo           = new("u_ceo",           "Anna CEO",       "ceo@acme.example",      null,              "Executive",   "CEO",               new[] { "CEO" });

    public static FakeIdentityProvider Default() => new(new[] { Wilson, WangManager, ChenVp, FinanceLead, PurchaseLead, Ceo });
}

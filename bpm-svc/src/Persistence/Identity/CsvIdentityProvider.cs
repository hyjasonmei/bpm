using Bpm.Application.Common.Identity;

namespace Bpm.Persistence.Identity;

/// Phase A IIdentityProvider: reads a CSV file once at construction and serves
/// lookups from memory. Schema (columns in order):
///   empId,name,email,manager,department,title,roles
/// where `roles` is a semicolon-separated list (e.g. "Finance;DEPT_HEAD" or "CEO").
/// Maps to spec.integrations.fieldMappings; the `roles` column is an addition
/// required by spec.approvals.fallback.role and spec.userTasks.permissions
/// (e.g. role:Finance, role:CEO, role:Purchase) which fieldMappings does not cover.
public sealed class CsvIdentityProvider : IIdentityProvider
{
    private readonly IReadOnlyList<Employee> _employees;
    private readonly Dictionary<string, Employee> _byId;

    public CsvIdentityProvider(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"Identity CSV not found at '{csvPath}'.", csvPath);

        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 1)
            throw new InvalidDataException($"Identity CSV '{csvPath}' is empty.");

        var headers = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        int IdxOf(string name) => headers.IndexOf(name);

        int iEmp = IdxOf("empid"), iName = IdxOf("name"), iEmail = IdxOf("email"),
            iMgr = IdxOf("manager"), iDept = IdxOf("department"), iTitle = IdxOf("title"),
            iRoles = IdxOf("roles");

        if (iEmp < 0 || iName < 0 || iEmail < 0 || iMgr < 0 || iDept < 0 || iTitle < 0)
            throw new InvalidDataException(
                $"Identity CSV '{csvPath}' is missing one of: empId,name,email,manager,department,title.");

        var list = new List<Employee>();
        for (var r = 1; r < lines.Length; r++)
        {
            var raw = lines[r];
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var c = SplitCsvLine(raw);
            if (c.Count <= iTitle) continue;

            var roles = iRoles >= 0 && iRoles < c.Count && !string.IsNullOrWhiteSpace(c[iRoles])
                ? c[iRoles].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>();

            list.Add(new Employee(
                EmployeeId: c[iEmp].Trim(),
                DisplayName: c[iName].Trim(),
                Email: c[iEmail].Trim(),
                ManagerId: string.IsNullOrWhiteSpace(c[iMgr]) ? null : c[iMgr].Trim(),
                Department: c[iDept].Trim(),
                Title: c[iTitle].Trim(),
                Roles: roles
            ));
        }

        _employees = list;
        _byId = list.ToDictionary(e => e.EmployeeId, StringComparer.Ordinal);
    }

    public Task<Employee?> FindByIdAsync(string employeeId, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(employeeId, out var e) ? e : null);

    public Task<Employee?> FindDepartmentHeadAsync(string department, CancellationToken ct = default)
    {
        var head = _employees.FirstOrDefault(e =>
            string.Equals(e.Department, department, StringComparison.Ordinal) &&
            e.Roles.Contains("DEPT_HEAD"));
        return Task.FromResult(head);
    }

    public Task<Employee?> FindByRoleAsync(string role, CancellationToken ct = default)
    {
        var match = _employees.FirstOrDefault(e => e.Roles.Contains(role));
        return Task.FromResult(match);
    }

    private static List<string> SplitCsvLine(string line)
    {
        return line.Split(',').ToList();
    }
}

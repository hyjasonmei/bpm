using Bpm.Domain.Entities.Org;

namespace Bpm.Application.Org;

/// Read-side org-chart queries used by the resolver. All methods return
/// null/empty rather than throw when the link is missing — callers turn that
/// into a structured ResolutionError when relevant.
public interface IOrgChartReader
{
    Task<User?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<User?> GetManagerAsync(Guid userId, CancellationToken ct = default);
    Task<Department?> GetDepartmentOfAsync(Guid userId, CancellationToken ct = default);
    Task<Department?> GetDepartmentAsync(Guid deptId, CancellationToken ct = default);
    Task<Department?> GetDepartmentParentAsync(Guid deptId, CancellationToken ct = default);
    Task<User?> GetDepartmentHeadAsync(Guid deptId, CancellationToken ct = default);

    /// Transitive group expansion. Members may themselves be groups; all
    /// nested members are flattened to user ids. Cycles abort the walk and
    /// surface as a Cycle ResolutionError on the caller.
    Task<GroupExpansion> ExpandGroupAsync(Guid groupId, CancellationToken ct = default);

    /// All Principal ids assigned to the named role (optionally scoped to
    /// a flow). Each principal may itself be a User/Group/Department.
    Task<IReadOnlyList<(Guid PrincipalId, Guid RoleId)>> GetRoleAssigneesAsync(
        string roleCode, string? flowCode = null, CancellationToken ct = default);
}

public sealed record GroupExpansion(IReadOnlySet<Guid> UserIds, IReadOnlyList<Guid>? CyclePath = null)
{
    public bool HasCycle => CyclePath is not null;
}

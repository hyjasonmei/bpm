namespace Bpm.Application.Org;

/// Read-side org-chart queries used by the resolver. After the
/// unify-user-store change, every method returns just the identifier
/// (Guid) it found, so the Application layer doesn't depend on
/// Persistence-side entity types. Returns null when the link is
/// missing — callers turn that into a structured ResolutionError when
/// relevant.
public interface IOrgChartReader
{
    Task<Guid?> GetUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetManagerIdAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetPrimaryDepartmentIdAsync(Guid userId, CancellationToken ct = default);
    Task<Guid?> GetDepartmentIdAsync(Guid deptId, CancellationToken ct = default);
    Task<Guid?> GetDepartmentParentIdAsync(Guid deptId, CancellationToken ct = default);
    Task<Guid?> GetDepartmentHeadIdAsync(Guid deptId, CancellationToken ct = default);

    /// Transitive group expansion. Members may themselves be groups; all
    /// nested members are flattened to user ids. Cycles abort the walk and
    /// surface as a Cycle ResolutionError on the caller.
    Task<GroupExpansion> ExpandGroupAsync(Guid groupId, CancellationToken ct = default);

    /// All Principal ids assigned to the named role (optionally scoped to
    /// a flow). Each principal may itself be a User/Group/Department.
    /// After unify-user-store, role lookup is by `Admin_Roles.Name` (not
    /// `Code` — admin's role table never had a Code column).
    Task<IReadOnlyList<(Guid PrincipalId, Guid RoleId)>> GetRoleAssigneesAsync(
        string roleName, string? flowCode = null, CancellationToken ct = default);
}

public sealed record GroupExpansion(IReadOnlySet<Guid> UserIds, IReadOnlyList<Guid>? CyclePath = null)
{
    public bool HasCycle => CyclePath is not null;
}

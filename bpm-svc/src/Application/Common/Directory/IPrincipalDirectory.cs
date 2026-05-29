namespace Bpm.Application.Common.Directory;

/// <summary>
/// Read-side principal lookup port used by chef-cooked Application
/// services (state machines / inbox providers / notification template
/// recipient resolution). Application can't reference Persistence's
/// <c>SharedPrincipal</c> entity directly — this port carries just the
/// fields chef-side code actually needs.
///
/// Implementation lives in Persistence (binds against the
/// SharedIdentity DbSets that mirror admin's <c>Admin_Principals</c>
/// / <c>Admin_Roles</c> / <c>Admin_PrincipalRoles</c> tables).
/// </summary>
public interface IPrincipalDirectory
{
    /// <summary>Single lookup. Returns <c>null</c> when the id is unknown.</summary>
    Task<PrincipalInfo?> GetByIdAsync(Guid principalId, CancellationToken ct = default);

    /// <summary>
    /// Batch lookup. Missing ids are simply absent from the returned dictionary —
    /// callers handle the absence (typically falling back to "—" in the UI).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PrincipalInfo>> GetManyAsync(
        IReadOnlyCollection<Guid> principalIds, CancellationToken ct = default);

    /// <summary>
    /// First active <see cref="PrincipalKind.User"/> assigned to a role by name.
    /// Encapsulates the role-name → role-id → principal-role join + the
    /// User+Active+NotDeleted filter so chef-side resolvers don't have to.
    /// Returns <c>null</c> when the role doesn't exist or has no User member.
    /// </summary>
    Task<Guid?> FindFirstUserInRoleAsync(string roleName, CancellationToken ct = default);
}

public enum PrincipalKind
{
    User = 0,
    Department = 1,
    Group = 2,
    Other = 3,
}

public sealed record PrincipalInfo(
    Guid Id,
    PrincipalKind Kind,
    string DisplayName,
    string? Email,
    bool Active);

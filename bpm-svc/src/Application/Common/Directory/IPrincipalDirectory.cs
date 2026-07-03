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

    /// <summary>
    /// Every active User assigned to a role (direct + dept/group inherited),
    /// ordered by display name. Used to notify a shared-role-queue step's whole
    /// membership. Empty when the role is unknown or has no active User member.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUsersInRoleAsync(string roleName, CancellationToken ct = default);

    /// <summary>
    /// Every role Code the user effectively holds — direct grants plus roles
    /// inherited via a Dept / Group membership (where the grant's
    /// <c>InheritToMembers</c> is set). The reverse of
    /// <see cref="FindFirstUserInRoleAsync"/>; used by the shared-role-queue
    /// authorization + pending-inbox checks (does this caller hold the role a
    /// case is pending on?). Returns an empty set for an unknown user.
    /// </summary>
    Task<IReadOnlySet<string>> GetRoleCodesForUserAsync(Guid userId, CancellationToken ct = default);
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

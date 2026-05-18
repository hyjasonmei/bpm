namespace Bpm.Admin.Application.Roles;

/// <summary>
/// One row of effective role lookup for a user.
/// </summary>
/// <param name="RoleId">The role granted.</param>
/// <param name="SourcePrincipalId">Which principal's assignment produced this row (user / dept / group).</param>
/// <param name="ViaInherit">True if the role reached the user through inherit_to_members (dept ancestor or group ancestor).</param>
public record EffectiveRole(Guid RoleId, Guid SourcePrincipalId, bool ViaInherit);

namespace Bpm.Admin.Api.Odata;

// Curated read/write models exposed over OData for system integration. These
// deliberately expose ONLY safe org-directory fields — credential/session/audit
// tables are never surfaced. CRUD maps back to the canonical entities honoring
// existing invariants (uniqueness, soft-delete, audit).

/// A person (Principal of type User). Passwords are never a property here — an
/// integration sets/resets a login password via the bound action
/// POST /odata/Users({id})/SetPassword {"password":"…"} so the secret never rides
/// on the entity body and never appears in reads or $metadata.
public sealed class OrgUser
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool Active { get; set; }
}

/// A department (Principal of type Dept).
public sealed class OrgDepartment
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool Active { get; set; }
}

/// A role definition.
public sealed class OrgRole
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;   // SCREAMING_SNAKE, unique
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
}

/// A user↔role assignment. Key = (PrincipalId, RoleId).
public sealed class OrgMembership
{
    public Guid PrincipalId { get; set; }
    public Guid RoleId { get; set; }
    public bool InheritToMembers { get; set; }
    public DateTime AssignedAt { get; set; }
}

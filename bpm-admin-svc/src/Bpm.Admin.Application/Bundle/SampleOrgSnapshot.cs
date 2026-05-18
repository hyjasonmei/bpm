namespace Bpm.Admin.Application.Bundle;

/// <summary>
/// In-memory snapshot of the sample org chart shipped inside a bundle.
/// Mirrors the shape of <c>sample-org.json</c> on disk. Plain DTO — no
/// EF / domain coupling so the BundleBuilder can be wired from any layer
/// (wizard payload, repro runner, integration tests).
/// </summary>
public sealed record SampleOrgSnapshot(
    IReadOnlyList<UserSnapshot> Users,
    IReadOnlyList<DepartmentSnapshot> Departments,
    IReadOnlyList<GroupSnapshot> Groups,
    IReadOnlyList<RoleAssignmentSnapshot> RoleAssignments);

public sealed record UserSnapshot(
    Guid Id,
    string Email,
    string FullName,
    Guid? ManagerId,
    Guid? DepartmentId);

public sealed record DepartmentSnapshot(
    Guid Id,
    string Code,
    string Name,
    Guid? ParentId,
    Guid? HeadUserId);

public sealed record GroupSnapshot(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<Guid> MemberPrincipalIds);

public sealed record RoleAssignmentSnapshot(
    string RoleCode,
    Guid PrincipalId,
    string Scope,
    string? ScopeRef);

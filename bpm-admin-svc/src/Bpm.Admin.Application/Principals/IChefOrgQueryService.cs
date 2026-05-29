namespace Bpm.Admin.Application.Principals;

/// <summary>
/// Read-only org-graph queries exposed to chef via MCP. Lets chef
/// sanity-check a spec's actor refs against the customer's live
/// principals / roles / department tree before baking the resolver
/// into per-flow C# code. None of the methods mutate state.
/// </summary>
public interface IChefOrgQueryService
{
    Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PrincipalSummaryDto>> ListPrincipalsAsync(
        string? roleName = null,
        string? kind = null,
        string? search = null,
        int take = 100,
        CancellationToken ct = default);

    Task<OrgWalkResultDto> WalkOrgAsync(
        Guid submitterUserId,
        string path,
        CancellationToken ct = default);
}

public record RoleSummaryDto(Guid Id, string Name, string? Description, int MemberCount, bool IsSystem);

public record PrincipalSummaryDto(
    Guid Id,
    string Kind,                   // 'user' | 'dept' | 'group' | 'role'
    string DisplayName,
    string? Email,
    bool Active,
    IReadOnlyList<string> Roles);  // role names (only populated for users)

public record OrgWalkResultDto(
    Guid StartUserId,
    string Path,
    IReadOnlyList<OrgWalkStep> Steps,
    IReadOnlyList<PrincipalSummaryDto> Final);

public record OrgWalkStep(
    string Segment,
    string Kind,                                       // 'user' | 'dept'
    IReadOnlyList<PrincipalSummaryDto> Resolved);

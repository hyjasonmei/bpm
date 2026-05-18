using Bpm.Admin.Domain.Principals;

namespace Bpm.Admin.Application.Principals;

public record PrincipalDto(
    Guid Id,
    PrincipalType Type,
    string DisplayName,
    string? Email,
    bool Active,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreatePrincipalRequest(
    PrincipalType Type,
    string DisplayName,
    string? Email);

public record UpdatePrincipalRequest(
    string? DisplayName,
    string? Email,
    bool? Active);

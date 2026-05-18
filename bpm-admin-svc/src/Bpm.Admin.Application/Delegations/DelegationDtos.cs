namespace Bpm.Admin.Application.Delegations;

public record DelegationDto(
    Guid Id,
    Guid DelegatorPrincipalId,
    Guid DelegateToUserId,
    DateTime StartAt,
    DateTime EndAt,
    bool Active,
    string? Reason);

public record CreateDelegationRequest(
    Guid DelegatorPrincipalId,
    Guid DelegateToUserId,
    DateTime StartAt,
    DateTime EndAt,
    string? Reason);

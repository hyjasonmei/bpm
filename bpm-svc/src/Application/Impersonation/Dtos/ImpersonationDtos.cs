using Bpm.Domain.Entities.Impersonation;

namespace Bpm.Application.Impersonation.Dtos;

public sealed record StartImpersonationResult(
    string Token,
    DateTime ExpiresAt,
    Guid SessionId,
    Guid TargetUserId,
    string TargetFullName);

public sealed record ImpersonationSessionDto(
    Guid Id,
    Guid ImpersonatorUserId,
    string ImpersonatorName,
    Guid TargetUserId,
    string TargetName,
    DateTime StartedAt,
    DateTime? EndedAt,
    EndReason? EndReason,
    string Reason);

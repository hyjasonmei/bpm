using Bpm.Application.Impersonation.Dtos;

namespace Bpm.Application.Impersonation;

public interface IImpersonationService
{
    Task<StartImpersonationResult> StartAsync(Guid impersonatorUserId, Guid targetUserId, string reason, bool callerIsAlreadyImpersonating, CancellationToken ct = default);
    Task EndAsync(Guid sessionId, Guid byUserId, CancellationToken ct = default);
    Task<ImpersonationSessionDto?> GetActiveAsync(Guid impersonatorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<ImpersonationSessionDto>> GetHistoryAsync(int days, CancellationToken ct = default);
    Task RevokeAsync(Guid sessionId, Guid byUserId, CancellationToken ct = default);
}

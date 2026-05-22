namespace Bpm.Admin.Application.Auth;

public record AuthenticatedSession(Guid SessionId, Guid UserId, string DisplayName, DateTime ExpiresAt);

public interface IAuthService
{
    Task<AuthenticatedSession?> LoginAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<bool> LogoutAsync(Guid sessionId, CancellationToken ct = default);
    Task<AuthenticatedSession?> ResolveSessionAsync(Guid sessionId, CancellationToken ct = default);
}

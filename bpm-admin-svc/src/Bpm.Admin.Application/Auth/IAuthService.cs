namespace Bpm.Admin.Application.Auth;

public record AuthenticatedSession(Guid SessionId, Guid UserId, string DisplayName, DateTime ExpiresAt);

public interface IAuthService
{
    /// <summary>
    /// Verify credentials and return the user + effective roles + primary dept,
    /// WITHOUT creating a server-side session. Used by the JWT login path: the
    /// caller mints a stateless token from this result. Returns null on bad
    /// credentials / unknown / inactive user.
    /// </summary>
    Task<AuthenticatedUser?> AuthenticateAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken ct = default);

    // ── Legacy server-side session API (cookie auth) — retained for optional
    // server-side revoke; no longer on the login path after unify-jwt. ──
    Task<AuthenticatedSession?> LoginAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<bool> LogoutAsync(Guid sessionId, CancellationToken ct = default);
    Task<AuthenticatedSession?> ResolveSessionAsync(Guid sessionId, CancellationToken ct = default);
}

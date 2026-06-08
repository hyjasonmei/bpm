namespace Bpm.Admin.Application.Auth;

public record LoginRequest(string Username, string Password);

/// <summary>
/// JWT login response. Shape mirrors bpm-svc's <c>AuthController.LoginResponse</c>
/// (token + expiry + user) so admin-ui stores and sends the token the same way
/// for both services.
/// </summary>
public record LoginResponse(
    string Token,
    DateTime ExpiresAt,
    Guid UserId,
    string DisplayName,
    IReadOnlyList<string> Roles,
    string? DepartmentCode);

public record CurrentUserResponse(Guid UserId, string DisplayName, string? Email);

/// <summary>
/// Result of verifying a username/password. No server-side session is created —
/// the JWT minted from this is stateless (logout is client-side token discard).
/// </summary>
public record AuthenticatedUser(
    Guid UserId,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles,
    string? DepartmentCode);

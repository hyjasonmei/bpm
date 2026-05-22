namespace Bpm.Admin.Application.Auth;

public record LoginRequest(string Username, string Password);
public record LoginResponse(Guid UserId, string DisplayName);
public record CurrentUserResponse(Guid UserId, string DisplayName, string? Email);

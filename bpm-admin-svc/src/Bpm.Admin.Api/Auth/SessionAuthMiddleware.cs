using System.Security.Claims;
using Bpm.Admin.Application.Auth;

namespace Bpm.Admin.Api.Auth;

public static class SessionAuthDefaults
{
    public const string CookieName = "fc_session";
    public const string AuthScheme = "FlowcookSession";
}

/// <summary>
/// Reads the session cookie, resolves it to a user via <see cref="IAuthService"/>,
/// and attaches a ClaimsPrincipal so [Authorize] works without ASP.NET Identity.
/// </summary>
public class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IAuthService authService)
    {
        if (context.Request.Cookies.TryGetValue(SessionAuthDefaults.CookieName, out var cookieValue) &&
            Guid.TryParse(cookieValue, out var sessionId))
        {
            var resolved = await authService.ResolveSessionAsync(sessionId, context.RequestAborted);
            if (resolved is not null)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, resolved.UserId.ToString()),
                    new Claim(ClaimTypes.Name, resolved.DisplayName),
                    new Claim("session_id", resolved.SessionId.ToString()),
                };
                var identity = new ClaimsIdentity(claims, SessionAuthDefaults.AuthScheme);
                context.User = new ClaimsPrincipal(identity);
            }
        }

        await _next(context);
    }
}

using System.Security.Claims;

namespace Bpm.Admin.Api.Auth;

public static class ChefAuthDefaults
{
    public const string Scheme = "FlowcookChef";
    public const string Role = "Chef";
    public const string ConfigKey = "Bpm:Chef:Token";
    /// <summary>
    /// Claim that carries which flow id (if any) the chef session is
    /// bound to. POC tokens are static so this stays null; future PRs
    /// can mint per-flow tokens and stamp this.
    /// </summary>
    public const string FlowIdClaim = "chef_flow_id";
}

/// <summary>
/// Reads <c>Authorization: Bearer &lt;token&gt;</c> on every request
/// and, when the token matches <c>Bpm:Chef:Token</c> in config, sets
/// the ClaimsPrincipal to a synthetic chef identity (role
/// <see cref="ChefAuthDefaults.Role"/>). Cookie-based user sessions
/// are unaffected — if the token is missing or wrong, the request
/// falls through with whatever the cookie middleware decided.
///
/// Runs *after* <see cref="SessionAuthMiddleware"/> so the chef token
/// can override an anonymous request without clobbering an already-
/// authenticated user (the chef token is intentionally narrower).
/// </summary>
public sealed class ChefTokenAuthMiddleware
{
    private readonly RequestDelegate _next;

    public ChefTokenAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, IConfiguration config)
    {
        // If the caller is already authenticated as a user, leave them
        // alone — chef and user auth never combine in one request.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var expected = config[ChefAuthDefaults.ConfigKey];
        if (string.IsNullOrEmpty(expected))
        {
            await _next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var token = header.Substring("Bearer ".Length).Trim();
        if (!FixedTimeEquals(token, expected))
        {
            await _next(context);
            return;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "chef"),
            new Claim(ClaimTypes.Role, ChefAuthDefaults.Role),
        };
        var identity = new ClaimsIdentity(claims, ChefAuthDefaults.Scheme);
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }

    /// <summary>Constant-time string comparison to avoid timing attacks.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

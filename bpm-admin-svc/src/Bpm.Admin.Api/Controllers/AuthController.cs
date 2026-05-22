using System.Security.Claims;
using Bpm.Admin.Api.Auth;
using Bpm.Admin.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Username and password required.");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        var session = await _auth.LoginAsync(req.Username, req.Password, ip, ua, ct);
        if (session is null) return Unauthorized();

        Response.Cookies.Append(SessionAuthDefaults.CookieName, session.SessionId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = session.ExpiresAt,
        });

        return Ok(new LoginResponse(session.UserId, session.DisplayName));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(SessionAuthDefaults.CookieName, out var cookie) &&
            Guid.TryParse(cookie, out var sessionId))
        {
            await _auth.LogoutAsync(sessionId, ct);
        }
        Response.Cookies.Delete(SessionAuthDefaults.CookieName);
        return NoContent();
    }

    [HttpGet("me")]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var nameClaim = User.FindFirstValue(ClaimTypes.Name);
        if (idClaim is null || nameClaim is null) return Unauthorized();
        return Ok(new CurrentUserResponse(Guid.Parse(idClaim), nameClaim, null));
    }
}

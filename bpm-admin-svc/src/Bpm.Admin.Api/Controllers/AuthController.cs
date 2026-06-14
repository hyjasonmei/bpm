using System.Security.Claims;
using Bpm.Admin.Api.Auth;
using Bpm.Admin.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Admin.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AdminJwtTokenService _jwt;

    public AuthController(IAuthService auth, AdminJwtTokenService jwt)
    {
        _auth = auth;
        _jwt = jwt;
    }

    // Anonymous: you can't hold a token before you log in. The only anonymous
    // entry point now that the API has a RequireAuthenticatedUser fallback policy.
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Username and password required.");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        var authed = await _auth.AuthenticateAsync(req.Username, req.Password, ip, ua, ct);
        if (authed is null) return Unauthorized();

        // Mint a token whose claims (sub/email/roles/...) bpm-svc also accepts,
        // so admin-ui can call both /api and /bpmsvc with this single bearer.
        var (token, expiresAt) = _jwt.MintForUser(
            authed.UserId, authed.Email, authed.DisplayName, authed.Roles, authed.DepartmentCode);

        return Ok(new LoginResponse(
            token, expiresAt, authed.UserId, authed.DisplayName, authed.Roles, authed.DepartmentCode));
    }

    /// <summary>Stateless logout — JWT TTL governs; the client discards the token.</summary>
    // Anonymous so logout never fails on an already-expired/invalid token.
    [AllowAnonymous]
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();

    [HttpGet("me")]
    public ActionResult<CurrentUserResponse> Me()
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        // MapInboundClaims maps the token's "sub" → NameIdentifier and "email"
        // → Email; "full_name" is a custom claim read directly.
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (idClaim is null || !Guid.TryParse(idClaim, out var uid)) return Unauthorized();
        var name = User.FindFirstValue("full_name") ?? User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var email = User.FindFirstValue(ClaimTypes.Email);
        return Ok(new CurrentUserResponse(uid, name, email));
    }
}

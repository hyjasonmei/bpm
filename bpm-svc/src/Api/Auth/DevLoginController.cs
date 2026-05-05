using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Auth;

[ApiController]
[Route("api/dev")]
[AllowAnonymous]
public sealed class DevLoginController(PersonaLoginService logins) : ControllerBase
{
    private static readonly string[] AllowedPersonas = ["employee", "manager", "finance", "it", "hr", "admin"];

    public sealed record LoginRequest(string PersonaCode);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        // In prod mode the dev-login back-door must not be reachable.
        var authMode = (Environment.GetEnvironmentVariable("BPM_AUTH_MODE") ?? "dev").ToLowerInvariant();
        if (authMode == "prod") return NotFound();

        if (string.IsNullOrWhiteSpace(req.PersonaCode))
            return BadRequest(new { error = "missing_persona_code" });
        if (!AllowedPersonas.Contains(req.PersonaCode))
            return BadRequest(new { error = "unknown_persona", allowed = AllowedPersonas });

        try
        {
            var result = await logins.LoginAsync(req.PersonaCode, ct);
            return Ok(new
            {
                token = result.Token,
                expiresAt = result.ExpiresAt,
                user = result.User,
            });
        }
        catch (PersonaLoginException ex)
        {
            return StatusCode(500, new { error = ex.Code, message = ex.Message });
        }
    }
}

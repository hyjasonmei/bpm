using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bpm.Api.Common;

[ApiController]
[Route("api/debug")]
[Authorize]
public sealed class DebugWhoamiController : ControllerBase
{
    [HttpGet("whoami")]
    public IActionResult Whoami()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToArray();
        var isAdmin = User.IsInRole("admin");
        var isHr = User.IsInRole("hr");
        return Ok(new { claims, isAdmin, isHr, name = User.Identity?.Name });
    }
}

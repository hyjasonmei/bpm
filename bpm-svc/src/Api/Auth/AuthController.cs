using Bpm.Application.Auth;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Auth;

/// <summary>
/// Real password login backed by the unified Admin_UserCredentials store.
/// Lives alongside /api/dev/login (which mints persona JWTs from a fixed
/// 6-persona enum for dev shortcuts). Production builds disable
/// /api/dev/login via BPM_AUTH_MODE=prod; /api/auth/login is the only
/// supported entry point.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(
    AppDbContext db,
    IPasswordHasher hasher,
    JwtTokenService jwt) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);

    public sealed record LoginResponse(string Token, DateTime ExpiresAt, AuthedUserDto User);

    public sealed record AuthedUserDto(
        Guid Id,
        string FullName,
        string Email,
        IReadOnlyList<string> Roles,
        string? DepartmentCode);

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Email) || string.IsNullOrWhiteSpace(req?.Password))
            return BadRequest(new { error = "missing_credentials" });

        var emailNorm = req.Email.Trim().ToLowerInvariant();
        var user = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Type == SharedPrincipalType.User
                                   && p.Email != null
                                   && p.Email.ToLower() == emailNorm, ct);
        if (user is null || !user.Active || user.DeletedAt != null)
            return Unauthorized(new { error = "invalid_credentials" });

        var credential = await db.SharedUserCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (credential is null || !hasher.Verify(req.Password, credential.PasswordHash))
            return Unauthorized(new { error = "invalid_credentials" });

        // Effective roles: direct PrincipalRole rows on the user. Inherited
        // roles via dept / group membership are out-of-scope for this first
        // login pass and get layered in by the role resolver later.
        var roleNames = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            where pr.PrincipalId == user.Id
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            select r.Code).ToListAsync(ct);

        var primaryDept = await (
            from ud in db.SharedUserDepts.AsNoTracking()
            where ud.UserId == user.Id && ud.IsPrimary
            join d in db.SharedPrincipals.AsNoTracking() on ud.DeptId equals d.Id
            select d.DisplayName).FirstOrDefaultAsync(ct);

        var (token, expiresAt) = jwt.MintForUnifiedUser(user, roleNames, primaryDept);
        return Ok(new LoginResponse(
            token,
            expiresAt,
            new AuthedUserDto(user.Id, user.DisplayName, user.Email ?? string.Empty, roleNames, primaryDept)));
    }

    /// <summary>Server-stateless logout — JWT TTL governs. Returns 204.</summary>
    [HttpPost("logout")]
    public IActionResult Logout() => NoContent();
}

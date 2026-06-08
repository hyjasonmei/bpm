using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Bpm.Admin.Api.Auth;

/// <summary>
/// Mints a JWT for an admin user authenticated against the Admin_* identity
/// store. The claim shape mirrors bpm-svc's <c>JwtTokenService.MintForUnifiedUser</c>
/// exactly (sub = user id, email, tenant_id, full_name, dept_code, repeated
/// "roles" claims) so the token validates on bpm-svc as well — that's what lets
/// admin-ui call both /api (admin-svc) and /bpmsvc (bpm-svc) cross-origin with a
/// single bearer, no cookie, no per-service login.
/// </summary>
public sealed class AdminJwtTokenService(JwtOptions options, IHostEnvironment env)
{
    public (string Token, DateTime ExpiresAt) MintForUser(
        Guid userId, string email, string displayName, IEnumerable<string> roleNames, string? deptCode)
    {
        var now = DateTime.UtcNow;
        var lifetime = env.IsDevelopment() ? options.ExpiryDev : options.ExpiryProd;
        var expires = now.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new("tenant_id", "default"),
            new("full_name", displayName),
        };
        if (!string.IsNullOrEmpty(deptCode))
            claims.Add(new Claim("dept_code", deptCode));
        foreach (var role in roleNames)
            claims.Add(new Claim("roles", role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

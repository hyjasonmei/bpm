using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Bpm.Domain.Entities.Org;
using Microsoft.IdentityModel.Tokens;

namespace Bpm.Api.Auth;

public sealed class JwtTokenService(JwtOptions options, IHostEnvironment env)
{
    public (string Token, DateTime ExpiresAt) Mint(User user, string personaCode, IEnumerable<string> systemRoleCodes)
    {
        var now = DateTime.UtcNow;
        var lifetime = env.IsDevelopment() ? options.ExpiryDev : options.ExpiryProd;
        var expires = now.Add(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("persona_code", personaCode),
            new("tenant_id", "default"),
            new("full_name", user.FullName),
        };
        foreach (var role in systemRoleCodes)
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

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, expires);
    }

    public (string Token, DateTime ExpiresAt) MintImpersonation(
        User target, IEnumerable<string> targetSystemRoles, Guid impersonatorUserId, Guid sessionId, TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;
        var ttl = lifetime ?? TimeSpan.FromMinutes(30);
        var expires = now.Add(ttl);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, target.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, target.Email),
            new("persona_code", "impersonated"),
            new("tenant_id", "default"),
            new("full_name", target.FullName),
            new("impersonated_by", impersonatorUserId.ToString()),
            new("imp_session_id", sessionId.ToString()),
        };
        foreach (var role in targetSystemRoles)
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

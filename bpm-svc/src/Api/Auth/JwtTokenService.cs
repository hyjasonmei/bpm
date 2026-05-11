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

    /// <summary>
    /// PR-J4 §6.2: mint a sandbox-persona token. Looks like a regular JWT
    /// (sub = persona, roles = persona's roles, full_name = persona) but
    /// also carries <c>actual_actor_id</c> + <c>actual_actor_email</c> +
    /// <c>sandbox_actor=true</c> so the audit interceptor can stamp the
    /// real tester onto every row the persona writes. Lifetime mirrors the
    /// dev expiry (8h) — long enough to run a UAT scenario without re-mint.
    /// </summary>
    public (string Token, DateTime ExpiresAt) IssueSandboxPersonaToken(
        Guid personaUserId,
        string personaEmail,
        string personaFullName,
        IEnumerable<string> personaRoles,
        Guid actualActorUserId,
        string actualActorEmail)
    {
        var now = DateTime.UtcNow;
        var expires = now.Add(TimeSpan.FromHours(8));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, personaUserId.ToString()),
            new(JwtRegisteredClaimNames.Email, personaEmail),
            new("persona_code", "sandbox_persona"),
            new("tenant_id", "default"),
            new("full_name", personaFullName),
            new("actual_actor_id", actualActorUserId.ToString()),
            new("actual_actor_email", actualActorEmail),
            new("sandbox_actor", "true"),
        };
        foreach (var role in personaRoles)
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

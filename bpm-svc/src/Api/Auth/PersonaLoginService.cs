using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Auth;

public sealed record LoginResult(string Token, DateTime ExpiresAt, UserSummary User);

public sealed record UserSummary(Guid Id, string FullName, string Email, string? DepartmentCode, string PersonaCode, IReadOnlyList<string> Roles);

public sealed class PersonaLoginException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Dev-only persona quick-switch. Maps a persona code → seeded admin-svc
/// user → real JWT minted via JwtTokenService.MintForUnifiedUser. Used
/// by the IdentitySwitcher dropdown in bpm-ui (dev mode); production
/// builds disable /api/dev/login via BPM_AUTH_MODE=prod.
/// </summary>
public sealed class PersonaLoginService(AppDbContext db, JwtTokenService tokens, PersonaMappingOptions personas)
{
    public async Task<LoginResult> LoginAsync(string personaCode, CancellationToken ct = default)
    {
        if (!personas.Map.TryGetValue(personaCode, out var mapping) || string.IsNullOrWhiteSpace(mapping))
            throw new PersonaLoginException("persona_mapping_missing", $"No user mapped to persona '{personaCode}' in Personas config");

        SharedPrincipal? user = null;
        if (Guid.TryParse(mapping, out var id))
        {
            user = await db.SharedPrincipals.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.Type == SharedPrincipalType.User, ct);
        }
        else
        {
            var emailNorm = mapping.Trim().ToLowerInvariant();
            user = await db.SharedPrincipals.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Type == SharedPrincipalType.User
                                       && p.Email != null
                                       && p.Email.ToLower() == emailNorm, ct);
        }

        if (user is null)
            throw new PersonaLoginException("seed_user_not_found", $"Persona '{personaCode}' maps to '{mapping}' but no matching admin-svc User row exists");

        var roleNames = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            where pr.PrincipalId == user.Id
            select r.Name).Distinct().ToListAsync(ct);

        var primaryDept = await (
            from ud in db.SharedUserDepts.AsNoTracking()
            where ud.UserId == user.Id && ud.IsPrimary
            join d in db.SharedPrincipals.AsNoTracking() on ud.DeptId equals d.Id
            select d.DisplayName).FirstOrDefaultAsync(ct);

        var (token, expires) = tokens.MintForUnifiedUser(user, roleNames, primaryDept);
        var summary = new UserSummary(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            primaryDept,
            personaCode,
            roleNames);
        return new LoginResult(token, expires, summary);
    }
}

using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Auth;

public sealed record LoginResult(string Token, DateTime ExpiresAt, UserSummary User);

public sealed record UserSummary(Guid Id, string FullName, string Email, string? DepartmentCode, string PersonaCode, IReadOnlyList<string> Roles);

public sealed class PersonaLoginException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class PersonaLoginService(AppDbContext db, JwtTokenService tokens, PersonaMappingOptions personas)
{
    public async Task<LoginResult> LoginAsync(string personaCode, CancellationToken ct = default)
    {
        if (!personas.Map.TryGetValue(personaCode, out var mapping) || string.IsNullOrWhiteSpace(mapping))
            throw new PersonaLoginException("persona_mapping_missing", $"No user mapped to persona '{personaCode}' in Personas config");

        User? user;
        if (Guid.TryParse(mapping, out var id))
        {
            user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == id, ct);
        }
        else
        {
            user = await db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Email == mapping, ct);
        }

        if (user is null)
            throw new PersonaLoginException("seed_user_not_found", $"Persona '{personaCode}' maps to '{mapping}' but no matching User row exists");

        var systemRoleCodes = await (
            from ra in db.RoleAssignments
            join r in db.Roles on ra.RoleId equals r.Id
            where ra.PrincipalId == user.Id && r.Scope == RoleScope.System
            select r.Code).Distinct().ToListAsync(ct);

        var (token, expires) = tokens.Mint(user, personaCode, systemRoleCodes);
        var summary = new UserSummary(user.Id, user.FullName, user.Email, user.Department?.Code, personaCode, systemRoleCodes);
        return new LoginResult(token, expires, summary);
    }
}

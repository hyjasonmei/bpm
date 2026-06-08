using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Auth;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Principals;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Auth;

public class AuthService : IAuthService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    private readonly AdminDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditLogger _audit;

    public AuthService(AdminDbContext db, IPasswordHasher hasher, IAuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var user = await _db.Principals
            .Where(p => p.Type == PrincipalType.User && p.Email == username && p.Active)
            .FirstOrDefaultAsync(ct);
        if (user is null || user.DeletedAt != null)
        {
            await _audit.LogAsync("login_fail", "session", null, null, null,
                after: new { username, reason = "user_not_found" }, ct: ct);
            return null;
        }

        var credential = await _db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (credential is null || !_hasher.Verify(password, credential.PasswordHash))
        {
            await _audit.LogAsync("login_fail", "session", null, user.Id, user.Id,
                after: new { username, reason = "bad_password" }, ct: ct);
            return null;
        }

        // Direct role assignments only — inherited roles (via dept/group) are
        // layered in later by the resolver, matching bpm-svc's login behaviour.
        var roleNames = await (
            from pr in _db.PrincipalRoles
            where pr.PrincipalId == user.Id
            join r in _db.Roles on pr.RoleId equals r.Id
            select r.Code).ToListAsync(ct);

        var primaryDept = await (
            from ud in _db.UserDepts
            where ud.UserId == user.Id && ud.IsPrimary
            join d in _db.Principals on ud.DeptId equals d.Id
            select d.DisplayName).FirstOrDefaultAsync(ct);

        await _audit.LogAsync("login", "session", null, user.Id, user.Id, ct: ct);

        return new AuthenticatedUser(user.Id, user.DisplayName, user.Email ?? string.Empty, roleNames, primaryDept);
    }

    public async Task<AuthenticatedSession?> LoginAsync(string username, string password, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        // Username is the principal email (v0 simplification: human-friendly identifier)
        var user = await _db.Principals
            .Where(p => p.Type == PrincipalType.User && p.Email == username && p.Active)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            await _audit.LogAsync("login_fail", "session", null, null, null,
                after: new { username, reason = "user_not_found" }, ct: ct);
            return null;
        }

        var credential = await _db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (credential is null || !_hasher.Verify(password, credential.PasswordHash))
        {
            await _audit.LogAsync("login_fail", "session", null, user.Id, user.Id,
                after: new { username, reason = "bad_password" }, ct: ct);
            return null;
        }

        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(SessionLifetime),
            IpAddress = ipAddress,
            UserAgent = userAgent,
        };
        _db.UserSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("login", "session", session.Id.ToString(), user.Id, user.Id, ct: ct);

        return new AuthenticatedSession(session.Id, user.Id, user.DisplayName, session.ExpiresAt);
    }

    public async Task<bool> LogoutAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return false;
        _db.UserSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("logout", "session", sessionId.ToString(), session.UserId, session.UserId, ct: ct);
        return true;
    }

    public async Task<AuthenticatedSession?> ResolveSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _db.UserSessions.Remove(session);
            await _db.SaveChangesAsync(ct);
            return null;
        }
        var user = await _db.Principals.FirstOrDefaultAsync(p => p.Id == session.UserId && p.Active, ct);
        if (user is null) return null;
        return new AuthenticatedSession(session.Id, user.Id, user.DisplayName, session.ExpiresAt);
    }
}

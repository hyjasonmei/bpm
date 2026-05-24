using Bpm.Api.Auth;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Impersonation;
using Bpm.Persistence;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Impersonation;

public sealed class JwtImpersonationTokenMinter(JwtTokenService jwt, AppDbContext db) : IImpersonationTokenMinter
{
    public (string Token, DateTime ExpiresAt) MintFor(Guid targetUserId, Guid impersonatorUserId, Guid sessionId)
    {
        var target = db.SharedPrincipals.AsNoTracking()
            .FirstOrDefault(p => p.Id == targetUserId && p.Type == SharedPrincipalType.User)
            ?? throw new NotFoundException("User", targetUserId);

        var roleNames = (from pr in db.SharedPrincipalRoles.AsNoTracking()
                         join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
                         where pr.PrincipalId == targetUserId
                         select r.Name).Distinct().ToList();

        return jwt.MintImpersonationFromShared(target, roleNames, impersonatorUserId, sessionId);
    }
}

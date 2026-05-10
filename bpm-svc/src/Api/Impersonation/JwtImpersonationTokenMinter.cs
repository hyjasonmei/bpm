using Bpm.Api.Auth;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Impersonation;
using Bpm.Domain.Entities.Authz;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Api.Impersonation;

public sealed class JwtImpersonationTokenMinter(JwtTokenService jwt, AppDbContext db) : IImpersonationTokenMinter
{
    public (string Token, DateTime ExpiresAt) MintFor(Guid targetUserId, Guid impersonatorUserId, Guid sessionId)
    {
        var target = db.Users.FirstOrDefault(u => u.Id == targetUserId)
            ?? throw new NotFoundException("User", targetUserId);

        var roles = db.RoleAssignments
            .Where(ra => ra.PrincipalId == targetUserId && ra.Role!.Scope == RoleScope.System)
            .Select(ra => ra.Role!.Code)
            .Distinct()
            .ToList();

        return jwt.MintImpersonation(target, roles, impersonatorUserId, sessionId);
    }
}

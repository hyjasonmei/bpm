using Bpm.Application.Common.Exceptions;
using Bpm.Application.Impersonation;
using Bpm.Application.Impersonation.Dtos;
using Bpm.Domain.Entities.Impersonation;
using Bpm.Persistence.SharedIdentity;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Persistence.Impersonation;

public sealed class ImpersonationService(AppDbContext db, IImpersonationTokenMinter minter) : IImpersonationService
{
    // Admin role name in admin-svc's seed (mirrors Bpm.Admin.Persistence.Seed.Seeder).
    // Renamed from "admin" → "SystemAdmin" by the unify-user-store change.
    private const string AdminRoleName = "SYSTEM_ADMIN";

    public async Task<StartImpersonationResult> StartAsync(
        Guid impersonatorUserId, Guid targetUserId, string reason, bool callerIsAlreadyImpersonating, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ConflictException("reason is required");
        if (impersonatorUserId == targetUserId)
            throw new ConflictException("cannot impersonate self");
        if (callerIsAlreadyImpersonating)
            throw new ConflictException("cannot nest impersonation; end the current session first");

        var hasOtherActive = await db.ImpersonationSessions
            .AnyAsync(s => s.ImpersonatorUserId == impersonatorUserId && s.EndedAt == null, ct);
        if (hasOtherActive)
            throw new ConflictException("you already have an active impersonation session; end it first");

        var callerIsAdmin = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            where pr.PrincipalId == impersonatorUserId && r.Code == AdminRoleName
            select pr).AnyAsync(ct);
        if (!callerIsAdmin)
            throw new ForbiddenException("only admins can start impersonation");

        var target = await db.SharedPrincipals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == targetUserId && p.Type == SharedPrincipalType.User, ct);
        if (target is null) throw new NotFoundException("User", targetUserId);
        if (!target.Active || target.DeletedAt != null) throw new ConflictException("target user is inactive");

        var session = new ImpersonationSession
        {
            ImpersonatorUserId = impersonatorUserId,
            TargetUserId = targetUserId,
            Reason = reason.Trim(),
            StartedAt = DateTime.UtcNow,
        };
        db.ImpersonationSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var (token, expiresAt) = minter.MintFor(targetUserId, impersonatorUserId, session.Id);
        return new StartImpersonationResult(token, expiresAt, session.Id, target.Id, target.DisplayName);
    }

    public async Task EndAsync(Guid sessionId, Guid byUserId, CancellationToken ct = default)
    {
        var s = await db.ImpersonationSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) return; // idempotent
        if (s.EndedAt is not null) return;
        if (s.ImpersonatorUserId != byUserId)
            throw new ForbiddenException("only the impersonator can end their own session via this endpoint");
        s.EndedAt = DateTime.UtcNow;
        s.EndReason = EndReason.ManualExit;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ImpersonationSessionDto?> GetActiveAsync(Guid impersonatorUserId, CancellationToken ct = default)
    {
        var s = await db.ImpersonationSessions.AsNoTracking()
            .Where(x => x.ImpersonatorUserId == impersonatorUserId && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;
        return await ToDto(s, ct);
    }

    public async Task<IReadOnlyList<ImpersonationSessionDto>> GetHistoryAsync(int days, CancellationToken ct = default)
    {
        if (days < 1) days = 1;
        if (days > 90) days = 90;
        var since = DateTime.UtcNow.AddDays(-days);

        // Lazy-mark AutoExpired: any session ending in the past whose JWT must have expired
        // (started > 30 min ago, EndedAt null) → AutoExpired
        var expiredCutoff = DateTime.UtcNow.AddMinutes(-30);
        var stale = await db.ImpersonationSessions
            .Where(x => x.EndedAt == null && x.StartedAt < expiredCutoff)
            .ToListAsync(ct);
        foreach (var s in stale)
        {
            s.EndedAt = DateTime.UtcNow;
            s.EndReason = EndReason.AutoExpired;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);

        var rows = await db.ImpersonationSessions.AsNoTracking()
            .Where(x => x.StartedAt >= since)
            .OrderByDescending(x => x.StartedAt)
            .ToListAsync(ct);

        var dtos = new List<ImpersonationSessionDto>(rows.Count);
        foreach (var s in rows) dtos.Add(await ToDto(s, ct));
        return dtos;
    }

    public async Task RevokeAsync(Guid sessionId, Guid byUserId, CancellationToken ct = default)
    {
        var callerIsAdmin = await (
            from pr in db.SharedPrincipalRoles.AsNoTracking()
            join r in db.SharedRoles.AsNoTracking() on pr.RoleId equals r.Id
            where pr.PrincipalId == byUserId && r.Code == AdminRoleName
            select pr).AnyAsync(ct);
        if (!callerIsAdmin) throw new ForbiddenException("only admins can revoke impersonation sessions");

        var s = await db.ImpersonationSessions.FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null) throw new NotFoundException("ImpersonationSession", sessionId);
        if (s.EndedAt is not null) throw new ConflictException("session already ended");
        s.EndedAt = DateTime.UtcNow;
        s.EndReason = EndReason.AdminRevoked;
        await db.SaveChangesAsync(ct);
    }

    private async Task<ImpersonationSessionDto> ToDto(ImpersonationSession s, CancellationToken ct)
    {
        var imp = await db.SharedPrincipals.AsNoTracking().FirstOrDefaultAsync(u => u.Id == s.ImpersonatorUserId, ct);
        var tgt = await db.SharedPrincipals.AsNoTracking().FirstOrDefaultAsync(u => u.Id == s.TargetUserId, ct);
        return new ImpersonationSessionDto(
            s.Id, s.ImpersonatorUserId, imp?.DisplayName ?? "(unknown)",
            s.TargetUserId, tgt?.DisplayName ?? "(unknown)",
            s.StartedAt, s.EndedAt, s.EndReason, s.Reason);
    }
}

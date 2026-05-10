using Bpm.Application.Common.Abstractions;
using Bpm.Domain.Common;
using Bpm.Domain.Entities.HrFlows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bpm.Persistence.Interceptors;

public sealed class AuditSaveChangesInterceptor(IClock clock, ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        ApplyAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void ApplyAudit(DbContext? context)
    {
        if (context is null) return;

        var now = clock.UtcNow;
        var by = currentUser.Id ?? "system";

        foreach (EntityEntry<AuditableEntity> entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.CreatedBy = by;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;
            }
        }

        foreach (EntityEntry<HrFlowAction> entry in context.ChangeTracker.Entries<HrFlowAction>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    $"HrFlowAction is append-only: {entry.State} is not permitted (Id={entry.Entity.Id}).");
        }

        // Stamp ImpersonatedByUserId onto IImpersonable entities at insert time.
        if (currentUser.ImpersonatedById is { } impId)
        {
            foreach (EntityEntry<IImpersonable> entry in context.ChangeTracker.Entries<IImpersonable>())
            {
                if (entry.State == EntityState.Added && entry.Entity.ImpersonatedByUserId is null)
                    entry.Entity.ImpersonatedByUserId = impId;
            }
        }
    }
}

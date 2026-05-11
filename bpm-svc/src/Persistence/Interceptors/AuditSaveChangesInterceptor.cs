using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Services;
using Bpm.Domain.Common;
using Bpm.Domain.Entities.HrFlows;
using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bpm.Persistence.Interceptors;

public sealed class AuditSaveChangesInterceptor(
    IClock clock,
    ICurrentUser currentUser,
    ISandboxActorContext? sandboxActor = null) : SaveChangesInterceptor
{
    // PR-J4 §6.5: when no ISandboxActorContext is supplied (e.g. test
    // fixtures, background jobs), fall back to the all-null system impl so
    // the stamping branch below cleanly skips. Keeps the existing
    // 2-arg ctor call sites in tests source-compatible.
    private readonly ISandboxActorContext _sandboxActor = sandboxActor ?? new SystemSandboxActor();

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

        foreach (EntityEntry<TaskHistory> entry in context.ChangeTracker.Entries<TaskHistory>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException(
                    $"TaskHistory is append-only: {entry.State} is not permitted (Id={entry.Entity.Id}).");
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

        // PR-J4 §6.5: stamp SandboxActualActor on ProcessTask + TaskHistory
        // when the request is acting through a sandbox-persona token. Both
        // entities already have the nullable Guid column (declared in PR-B,
        // see ProcessTask.cs / TaskHistory.cs), so no migration is needed.
        // We only stamp if the field hasn't already been set explicitly by
        // the runtime — that gives the runtime an escape hatch to override
        // (e.g. background-resumption code paths).
        if (_sandboxActor.IsSandboxActor && _sandboxActor.ActualActorUserId is { } actualId)
        {
            foreach (EntityEntry<ProcessTask> entry in context.ChangeTracker.Entries<ProcessTask>())
            {
                if (entry.State == EntityState.Added && entry.Entity.SandboxActualActor is null)
                    entry.Entity.SandboxActualActor = actualId;
            }
            foreach (EntityEntry<TaskHistory> entry in context.ChangeTracker.Entries<TaskHistory>())
            {
                if (entry.State == EntityState.Added && entry.Entity.SandboxActualActor is null)
                    entry.Entity.SandboxActualActor = actualId;
            }
        }
    }
}

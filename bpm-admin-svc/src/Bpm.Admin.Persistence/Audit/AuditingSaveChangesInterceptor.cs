using System.Text.Json;
using Bpm.Admin.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bpm.Admin.Persistence.Audit;

/// <summary>
/// SaveChanges interceptor that emits an AuditEvent row for every Added /
/// Modified / Deleted entity implementing <see cref="IAuditable"/>.
/// Captures before / after JSON snapshots. Records <c>source_system=admin</c>.
/// </summary>
public class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AdminDbContext db)
        {
            AppendAuditEntries(db);
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AdminDbContext db)
        {
            AppendAuditEntries(db);
        }
        return base.SavingChanges(eventData, result);
    }

    private static void AppendAuditEntries(AdminDbContext db)
    {
        var entries = db.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var now = DateTime.UtcNow;
        var newEvents = new List<AuditEvent>(entries.Count);
        foreach (var entry in entries)
        {
            var (actionType, before, after) = ExtractSnapshot(entry);
            var targetType = entry.Entity.GetType().Name;
            var targetId = TryExtractId(entry);
            newEvents.Add(new AuditEvent
            {
                EventId = Guid.NewGuid(),
                ActorUserId = null,
                ActorPrincipalId = null,
                ActionType = "entity_" + actionType,
                TargetType = targetType,
                TargetId = targetId,
                Timestamp = now,
                BeforeJson = before,
                AfterJson = after,
                SourceSystem = "admin",
            });
        }

        db.AuditEvents.AddRange(newEvents);
    }

    private static (string action, string? before, string? after) ExtractSnapshot(EntityEntry entry)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                return ("created", null, SerializeEntity(entry.CurrentValues));
            case EntityState.Modified:
                return ("updated", SerializeEntity(entry.OriginalValues), SerializeEntity(entry.CurrentValues));
            case EntityState.Deleted:
                return ("deleted", SerializeEntity(entry.OriginalValues), null);
            default:
                return ("unknown", null, null);
        }
    }

    private static string SerializeEntity(PropertyValues values)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in values.Properties)
        {
            dict[property.Name] = values[property.Name];
        }
        return JsonSerializer.Serialize(dict, JsonOptions);
    }

    private static string? TryExtractId(EntityEntry entry)
    {
        var idProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (idProperty is null) return null;
        var value = entry.Property(idProperty.Name).CurrentValue;
        return value?.ToString();
    }
}

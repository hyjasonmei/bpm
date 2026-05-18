using System.Text.Json;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Domain.Audit;

namespace Bpm.Admin.Persistence.Audit;

public class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AdminDbContext _db;

    public AuditLogger(AdminDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string actionType,
        string targetType,
        string? targetId,
        Guid? actorUserId,
        Guid? actorPrincipalId,
        object? before = null,
        object? after = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            EventId = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActorPrincipalId = actorPrincipalId,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = targetId,
            Timestamp = DateTime.UtcNow,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions),
            SourceSystem = "admin",
            Reason = reason,
        });
        await _db.SaveChangesAsync(ct);
    }
}

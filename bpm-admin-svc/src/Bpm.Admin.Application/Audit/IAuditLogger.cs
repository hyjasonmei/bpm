namespace Bpm.Admin.Application.Audit;

public interface IAuditLogger
{
    Task LogAsync(
        string actionType,
        string targetType,
        string? targetId,
        Guid? actorUserId,
        Guid? actorPrincipalId,
        object? before = null,
        object? after = null,
        string? reason = null,
        CancellationToken ct = default);
}

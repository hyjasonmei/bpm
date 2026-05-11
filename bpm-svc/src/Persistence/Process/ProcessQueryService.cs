using System.Globalization;
using System.Text.Json;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime.Dtos;
using Bpm.Application.Process.Runtime.Queries;
using Bpm.Domain.Entities.Process;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Process;

/// <summary>
/// Read-side projection over <see cref="AppDbContext"/>. Pure queries — no
/// state mutation, no transactions, no hook dispatch.
/// </summary>
public sealed class ProcessQueryService(AppDbContext db) : IProcessQueryService
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public async Task<ProcessInstanceDto> GetInstanceAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default)
    {
        var instance = await db.ProcessInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", instanceId);

        // V1 rule: only initiator may read. Tenant-admin override is a TODO
        // — once a roles claim is reliable on the request principal, expand.
        if (instance.InitiatorUserId != requesterUserId)
            throw new ForbiddenException($"user {requesterUserId} cannot read instance {instanceId}");

        var tasks = await db.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == instanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        return ToInstanceDto(instance, tasks);
    }

    public async Task<HistoryPage> GetHistoryPageAsync(Guid instanceId, Guid requesterUserId, string? cursor, int limit, CancellationToken ct = default)
    {
        var instance = await db.ProcessInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", instanceId);
        if (instance.InitiatorUserId != requesterUserId)
            throw new ForbiddenException($"user {requesterUserId} cannot read history for instance {instanceId}");

        var clamped = Math.Clamp(limit, 1, 200);

        // Cursor encodes (CreatedAt|Id). The Id tiebreak is essential because
        // start-of-instance writes multiple history rows in the same SaveChanges
        // → identical CreatedAt timestamps. CreatedAt-only would skip rows.
        DateTime? cursorTs = null;
        Guid? cursorId = null;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            var parts = cursor.Split('|', 2);
            if (parts.Length != 2
                || !DateTime.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind, out var parsedTs)
                || !Guid.TryParse(parts[1], out var parsedId))
                throw new ConflictException("invalid cursor");
            cursorTs = parsedTs.Kind switch
            {
                DateTimeKind.Utc => parsedTs,
                DateTimeKind.Local => parsedTs.ToUniversalTime(),
                _ => DateTime.SpecifyKind(parsedTs, DateTimeKind.Utc),
            };
            cursorId = parsedId;
        }

        var query = db.TaskHistory.AsNoTracking()
            .Where(h => h.ProcessInstanceId == instanceId);
        if (cursorTs is not null && cursorId is not null)
        {
            // Strict tuple "greater than": (CreatedAt > ts) OR (CreatedAt == ts AND Id > id).
            var ts = cursorTs.Value;
            var id = cursorId.Value;
            query = query.Where(h =>
                h.CreatedAt > ts
                || (h.CreatedAt == ts && h.Id.CompareTo(id) > 0));
        }

        var rows = await query
            .OrderBy(h => h.CreatedAt).ThenBy(h => h.Id)
            .Take(clamped + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > clamped)
        {
            var last = rows[clamped - 1];
            nextCursor = $"{last.CreatedAt.ToString("O", CultureInfo.InvariantCulture)}|{last.Id}";
            rows = rows.Take(clamped).ToList();
        }

        var items = rows.Select(ToHistoryDto).ToList();
        return new HistoryPage(items, nextCursor);
    }

    public async Task<IReadOnlyList<ProcessTaskDto>> GetMineAsync(Guid requesterUserId, string status, int limit, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 200);
        var query = db.ProcessTasks.AsNoTracking()
            .Where(t => t.ActualAssigneeUserId == requesterUserId);

        var normalized = (status ?? "open").ToLowerInvariant();
        query = normalized switch
        {
            "open" => query.Where(t => t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress),
            "completed" => query.Where(t => t.Status == TaskStatus.Completed || t.Status == TaskStatus.Cancelled),
            "all" => query,
            _ => throw new ConflictException($"unsupported status filter '{status}' (allowed: open|completed|all)"),
        };

        var rows = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(clamped)
            .ToListAsync(ct);

        return rows.Select(ToTaskDto).ToList();
    }

    public async Task<TaskWithFormDto> GetTaskAsync(Guid taskId, Guid requesterUserId, CancellationToken ct = default)
    {
        var task = await db.ProcessTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("ProcessTask", taskId);

        var instance = await db.ProcessInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == task.ProcessInstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", task.ProcessInstanceId);

        if (task.ActualAssigneeUserId != requesterUserId
            && instance.InitiatorUserId != requesterUserId)
            throw new ForbiddenException($"user {requesterUserId} cannot read task {taskId}");

        return new TaskWithFormDto(
            ToTaskDto(task),
            ParseJson(instance.CurrentFormDataJson),
            instance.SpecCode,
            instance.Id);
    }

    // ----- mappers -----

    internal static ProcessInstanceDto ToInstanceDto(ProcessInstance i, IReadOnlyList<ProcessTask> tasks) => new(
        i.Id, i.SpecCode, i.SpecVersion, i.InitiatorUserId, i.Status,
        ParseJson(i.CurrentFormDataJson), i.StartedAt, i.CompletedAt, i.CancelledAt, i.CancelReason,
        tasks.Select(ToTaskDto).ToList());

    internal static ProcessTaskDto ToTaskDto(ProcessTask t) => new(
        t.Id, t.NodeId, t.NodeKind, t.OriginalAssigneeUserId, t.ActualAssigneeUserId,
        t.Status, t.DueAt, t.ClaimedAt, t.CompletedAt, t.Decision, t.Comment);

    internal static TaskHistoryDto ToHistoryDto(TaskHistory h) => new(
        h.Id, h.TaskId, h.EventType, h.ActorUserId, ParseJson(h.PayloadJson), h.CreatedAt);

    internal static JsonElement ParseJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EmptyObject;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return EmptyObject;
        }
    }
}

using System.Globalization;
using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Runtime;
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
public sealed class ProcessQueryService(AppDbContext db, IClock clock) : IProcessQueryService
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

        if (rows.Count == 0) return Array.Empty<ProcessTaskDto>();

        // PR-L3: ProcessTaskDto carries SpecCode so the inbox can route a click
        // to the matching FormCode without a per-row instance fetch. Pull the
        // distinct instance ids in one round-trip.
        var instanceIds = rows.Select(t => t.ProcessInstanceId).Distinct().ToList();
        var specByInstance = await db.ProcessInstances.AsNoTracking()
            .Where(i => instanceIds.Contains(i.Id))
            .Select(i => new { i.Id, i.SpecCode })
            .ToDictionaryAsync(x => x.Id, x => x.SpecCode, ct);

        return rows
            .Select(t => ToTaskDto(t, specByInstance.TryGetValue(t.ProcessInstanceId, out var sc) ? sc : string.Empty))
            .ToList();
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
            ToTaskDto(task, instance.SpecCode),
            ParseJson(instance.CurrentFormDataJson),
            instance.SpecCode,
            instance.Id);
    }

    public async Task<IReadOnlyList<ActiveCaseDto>> GetActiveCasesAsync(
        string? specCode = null,
        int? maxAgeDays = null,
        bool breachOnly = false,
        int limit = 100,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 500);
        var now = clock.UtcNow;

        // Active = Running | Errored. Cancelled / Completed live in the
        // completed-cases query (PR-K5). We project the join client-side
        // because EF + SQLite needs a tractable shape — we expect the active
        // set to be small (<a few hundred typically).
        var instanceQuery = db.ProcessInstances.AsNoTracking()
            .Where(i => i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Errored);

        if (!string.IsNullOrWhiteSpace(specCode))
            instanceQuery = instanceQuery.Where(i => i.SpecCode == specCode);

        if (maxAgeDays is int days && days > 0)
        {
            var cutoff = now.AddDays(-days);
            instanceQuery = instanceQuery.Where(i => i.StartedAt >= cutoff);
        }

        var instances = await instanceQuery
            .OrderByDescending(i => i.LastActivityAt)
            .Take(clamped * 2) // over-fetch so breachOnly can filter without an extra round-trip
            .ToListAsync(ct);

        if (instances.Count == 0)
            return Array.Empty<ActiveCaseDto>();

        var instanceIds = instances.Select(i => i.Id).ToList();
        var openTasks = await db.ProcessTasks.AsNoTracking()
            .Where(t => instanceIds.Contains(t.ProcessInstanceId)
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync(ct);

        var initiatorIds = instances.Select(i => i.InitiatorUserId).Distinct().ToList();
        var assigneeIds = openTasks
            .Select(t => t.ActualAssigneeUserId)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        var allUserIds = initiatorIds.Concat(assigneeIds).Distinct().ToList();

        var users = await db.SharedPrincipals.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        var userEmails = users.ToDictionary(u => u.Id, u => u.Email ?? string.Empty);

        var openByInstance = openTasks
            .GroupBy(t => t.ProcessInstanceId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ActiveCaseDto>(instances.Count);
        foreach (var i in instances)
        {
            openByInstance.TryGetValue(i.Id, out var open);
            open ??= new List<ProcessTask>();
            var openCount = open.Count;
            var hasBreach = open.Any(t => t.DueAt is DateTime due && due < now);
            if (breachOnly && !hasBreach) continue;

            Guid? singleAssignee = null;
            string? singleAssigneeEmail = null;
            if (openCount == 1 && open[0].ActualAssigneeUserId is Guid uid)
            {
                singleAssignee = uid;
                userEmails.TryGetValue(uid, out singleAssigneeEmail);
            }

            userEmails.TryGetValue(i.InitiatorUserId, out var initEmail);

            rows.Add(new ActiveCaseDto(
                Id: i.Id,
                SpecCode: i.SpecCode,
                SpecVersion: i.SpecVersion,
                InitiatorUserId: i.InitiatorUserId,
                InitiatorEmail: initEmail,
                Status: i.Status,
                StartedAt: i.StartedAt,
                LastActivityAt: i.LastActivityAt,
                OpenTaskCount: openCount,
                HasBreach: hasBreach,
                CurrentAssigneeUserId: singleAssignee,
                CurrentAssigneeEmail: singleAssigneeEmail));

            if (rows.Count >= clamped) break;
        }

        return rows;
    }

    public async Task<CompletedCasesPage> GetCompletedCasesAsync(
        string? specCode = null,
        DateTime? completedAfter = null,
        string? status = null,
        int limit = 100,
        string? cursor = null,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 500);

        // Status filter: default = both Completed + Cancelled.
        var normalized = (status ?? string.Empty).ToLowerInvariant();
        var includeCompleted = normalized != "cancelled";
        var includeCancelled = normalized != "completed";
        if (!includeCompleted && !includeCancelled)
        {
            // Belt-and-suspenders — current parser can't fall here, but a
            // future "all=false" misuse would otherwise return an inscrutable
            // empty list. Restore the default both-status behaviour.
            includeCompleted = true;
            includeCancelled = true;
        }

        var query = db.ProcessInstances.AsNoTracking()
            .Where(i => i.Status == InstanceStatus.Completed || i.Status == InstanceStatus.Cancelled);

        if (includeCompleted && !includeCancelled)
            query = query.Where(i => i.Status == InstanceStatus.Completed);
        else if (!includeCompleted && includeCancelled)
            query = query.Where(i => i.Status == InstanceStatus.Cancelled);

        if (!string.IsNullOrWhiteSpace(specCode))
            query = query.Where(i => i.SpecCode == specCode);

        if (completedAfter is DateTime after)
        {
            // Apply against whichever terminal timestamp the row carries.
            var afterUtc = after.Kind switch
            {
                DateTimeKind.Utc => after,
                DateTimeKind.Local => after.ToUniversalTime(),
                _ => DateTime.SpecifyKind(after, DateTimeKind.Utc),
            };
            query = query.Where(i =>
                (i.CompletedAt != null && i.CompletedAt >= afterUtc) ||
                (i.CancelledAt != null && i.CancelledAt >= afterUtc));
        }

        // Cursor encodes (TerminalAt|Id) — descending sort means the cursor
        // selects rows with TerminalAt < ts OR (TerminalAt == ts AND Id > id).
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

        // Pull the candidate set (overfetched) and project the terminal
        // timestamp client-side. EF/SQLite chokes on a per-row CASE inside an
        // ORDER BY when nullable DateTime is involved, and the terminal-cases
        // set is bounded enough that this is fine.
        var candidates = await query.ToListAsync(ct);
        var ordered = candidates
            .Select(i => new
            {
                Instance = i,
                TerminalAt = i.CompletedAt ?? i.CancelledAt ?? i.StartedAt,
            })
            .OrderByDescending(x => x.TerminalAt).ThenByDescending(x => x.Instance.Id)
            .ToList();

        if (cursorTs is not null && cursorId is not null)
        {
            var ts = cursorTs.Value;
            var id = cursorId.Value;
            // Strict tuple "less than" for descending order: (TerminalAt < ts)
            // OR (TerminalAt == ts AND Id < id).
            ordered = ordered.Where(x =>
                x.TerminalAt < ts
                || (x.TerminalAt == ts && x.Instance.Id.CompareTo(id) < 0))
                .ToList();
        }

        var page = ordered.Take(clamped + 1).ToList();
        string? nextCursor = null;
        if (page.Count > clamped)
        {
            var last = page[clamped - 1];
            nextCursor = $"{last.TerminalAt.ToString("O", CultureInfo.InvariantCulture)}|{last.Instance.Id}";
            page = page.Take(clamped).ToList();
        }

        // Resolve initiator emails in one round-trip.
        var initiatorIds = page.Select(p => p.Instance.InitiatorUserId).Distinct().ToList();
        var emails = await db.SharedPrincipals.AsNoTracking()
            .Where(u => initiatorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        var emailLookup = emails.ToDictionary(u => u.Id, u => u.Email ?? string.Empty);

        var rows = page.Select(p =>
        {
            var i = p.Instance;
            emailLookup.TryGetValue(i.InitiatorUserId, out var initEmail);
            DateTime? terminalAt = i.CompletedAt ?? i.CancelledAt;
            TimeSpan? cycle = terminalAt is DateTime t ? t - i.StartedAt : null;
            return new CompletedCaseDto(
                Id: i.Id,
                SpecCode: i.SpecCode,
                SpecVersion: i.SpecVersion,
                InitiatorUserId: i.InitiatorUserId,
                InitiatorEmail: initEmail,
                Status: i.Status,
                StartedAt: i.StartedAt,
                CompletedAt: i.CompletedAt,
                CancelledAt: i.CancelledAt,
                CycleTime: cycle,
                CancelReason: i.CancelReason);
        }).ToList();

        return new CompletedCasesPage(rows, nextCursor);
    }

    public async Task<IReadOnlyList<MyInstanceSummaryDto>> GetMyInstancesAsync(
        Guid initiatorUserId,
        string status,
        int limit,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(limit, 1, 200);

        var query = db.ProcessInstances.AsNoTracking()
            .Where(i => i.InitiatorUserId == initiatorUserId);

        var normalized = (status ?? "all").ToLowerInvariant();
        query = normalized switch
        {
            "active" => query.Where(i => i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Errored),
            "completed" => query.Where(i => i.Status == InstanceStatus.Completed || i.Status == InstanceStatus.Cancelled),
            "all" => query,
            _ => throw new ConflictException($"unsupported status filter '{status}' (allowed: active|completed|all)"),
        };

        var instances = await query
            .OrderByDescending(i => i.LastActivityAt)
            .Take(clamped)
            .ToListAsync(ct);

        if (instances.Count == 0) return Array.Empty<MyInstanceSummaryDto>();

        // Open-task counts in one round-trip; attach to the matching instance.
        var instanceIds = instances.Select(i => i.Id).ToList();
        var openTasks = await db.ProcessTasks.AsNoTracking()
            .Where(t => instanceIds.Contains(t.ProcessInstanceId)
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .Select(t => new { t.ProcessInstanceId, t.NodeId, t.CreatedAt })
            .ToListAsync(ct);

        var openByInstance = openTasks
            .GroupBy(t => t.ProcessInstanceId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).ToList());

        var rows = new List<MyInstanceSummaryDto>(instances.Count);
        foreach (var i in instances)
        {
            openByInstance.TryGetValue(i.Id, out var open);
            open ??= [];
            string? currentNodeLabel = null;
            // Best-effort: parse the spec snapshot once per instance and look
            // up the first open task's node label. SpecSnapshot is cheap (one
            // JsonDocument.Parse per call), and the v1 inbox row is the only
            // place we surface "where is this case sitting".
            if (open.Count > 0 && !string.IsNullOrWhiteSpace(i.SpecSnapshotJson))
            {
                try
                {
                    using var snap = new SpecSnapshot(i.SpecSnapshotJson);
                    var node = snap.GetNode(open[0].NodeId);
                    currentNodeLabel = node?.Label;
                }
                catch
                {
                    // Spec snapshot parse fail → leave label null. Don't fail
                    // the whole list because one row's snapshot is malformed.
                }
            }

            rows.Add(new MyInstanceSummaryDto(
                Id: i.Id,
                SpecCode: i.SpecCode,
                SpecVersion: i.SpecVersion,
                Status: i.Status,
                StartedAt: i.StartedAt,
                CompletedAt: i.CompletedAt,
                LastActivityAt: i.LastActivityAt,
                OpenTaskCount: open.Count,
                CurrentNodeLabel: currentNodeLabel));
        }

        return rows;
    }

    public async Task<LiveCaseDetailDto> GetCaseDetailAsync(
        Guid instanceId,
        int historyLimit = 20,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(historyLimit, 1, 200);

        var instance = await db.ProcessInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", instanceId);

        var tasks = await db.ProcessTasks.AsNoTracking()
            .Where(t => t.ProcessInstanceId == instanceId
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);

        // Most-recent N. We pull DESC then (after mapping) reverse to ascending
        // so the UI can render bottom-up newest-first either way; the API
        // contract states "most recent first" so we keep DESC ordering in the
        // returned list.
        var history = await db.TaskHistory.AsNoTracking()
            .Where(h => h.ProcessInstanceId == instanceId)
            .OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id)
            .Take(clamped)
            .ToListAsync(ct);

        return new LiveCaseDetailDto(
            ToInstanceDto(instance, tasks),
            history.Select(ToHistoryDto).ToList());
    }

    // ----- mappers -----

    internal static ProcessInstanceDto ToInstanceDto(ProcessInstance i, IReadOnlyList<ProcessTask> tasks) => new(
        i.Id, i.SpecCode, i.SpecVersion, i.InitiatorUserId, i.Status,
        ParseJson(i.CurrentFormDataJson), i.StartedAt, i.CompletedAt, i.CancelledAt, i.CancelReason,
        tasks.Select(t => ToTaskDto(t, i.SpecCode)).ToList());

    internal static ProcessTaskDto ToTaskDto(ProcessTask t, string specCode) => new(
        t.Id, t.NodeId, t.NodeKind, t.OriginalAssigneeUserId, t.ActualAssigneeUserId,
        t.Status, t.DueAt, t.ClaimedAt, t.CompletedAt, t.Decision, t.Comment, specCode);

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

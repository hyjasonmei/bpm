using System.Text.Json;
using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class FlowGroupService : IFlowGroupService
{
    private readonly AdminDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public FlowGroupService(AdminDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FlowGroupDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.FlowGroups
            .AsNoTracking()
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Code)
            .ToListAsync(ct);

        var counts = await _db.Flows
            .AsNoTracking()
            .Where(f => f.GroupId != null)
            .GroupBy(f => f.GroupId!.Value)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countMap = counts.ToDictionary(x => x.GroupId, x => x.Count);

        return rows.Select(g => ToDto(g, countMap.GetValueOrDefault(g.Id, 0))).ToList();
    }

    public async Task<FlowGroupDto> CreateAsync(CreateFlowGroupRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        Validate(req.Code, req.DisplayName);

        var clash = await _db.FlowGroups.AnyAsync(g => g.Code == req.Code, ct);
        if (clash) throw new FlowLifecycleException($"flow group code '{req.Code}' already in use");

        var row = new FlowGroup
        {
            Id = Guid.NewGuid(),
            Code = req.Code,
            DisplayNameJson = JsonSerializer.Serialize(req.DisplayName),
            SortOrder = req.SortOrder,
            Icon = string.IsNullOrWhiteSpace(req.Icon) ? null : req.Icon,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
        };
        _db.FlowGroups.Add(row);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_group_created",
            targetType: "flow_group",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            after: new { row.Code, row.SortOrder, row.Icon },
            ct: ct);

        return ToDto(row, 0);
    }

    public async Task<FlowGroupDto> UpdateAsync(Guid id, UpdateFlowGroupRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await _db.FlowGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new FlowLifecycleException($"flow group {id} not found");

        var before = new { row.Code, row.SortOrder, row.Icon };

        if (req.Code is not null && req.Code != row.Code)
        {
            Validate(req.Code, req.DisplayName ?? ReadDisplayName(row));
            var clash = await _db.FlowGroups.AnyAsync(g => g.Code == req.Code && g.Id != id, ct);
            if (clash) throw new FlowLifecycleException($"flow group code '{req.Code}' already in use");
            row.Code = req.Code;
        }
        if (req.DisplayName is not null)
        {
            Validate(row.Code, req.DisplayName);
            row.DisplayNameJson = JsonSerializer.Serialize(req.DisplayName);
        }
        if (req.SortOrder.HasValue) row.SortOrder = req.SortOrder.Value;
        if (req.Icon is not null) row.Icon = string.IsNullOrWhiteSpace(req.Icon) ? null : req.Icon;
        row.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_group_updated",
            targetType: "flow_group",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: new { row.Code, row.SortOrder, row.Icon },
            ct: ct);

        var count = await _db.Flows.CountAsync(f => f.GroupId == row.Id, ct);
        return ToDto(row, count);
    }

    public async Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await _db.FlowGroups.FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new FlowLifecycleException($"flow group {id} not found");

        row.DeletedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: "flow_group_deleted",
            targetType: "flow_group",
            targetId: row.Id.ToString(),
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: new { row.Code, row.SortOrder, row.Icon },
            ct: ct);
    }

    private static void Validate(string code, IReadOnlyDictionary<string, string> displayName)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new FlowLifecycleException("code required");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9_-]+$"))
            throw new FlowLifecycleException("code must be lowercase ascii, digits, hyphen or underscore");
        if (!displayName.TryGetValue("zh-TW", out var zh) || string.IsNullOrWhiteSpace(zh))
            throw new FlowLifecycleException("displayName must include a non-empty zh-TW entry");
    }

    private static IReadOnlyDictionary<string, string> ReadDisplayName(FlowGroup row)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(row.DisplayNameJson)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static FlowGroupDto ToDto(FlowGroup row, int flowCount)
        => new(row.Id, row.Code, ReadDisplayName(row), row.SortOrder, row.Icon, flowCount);
}

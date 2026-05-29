using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Flows;
using EnvEntity = Bpm.Admin.Domain.Flows.Environment;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class EnvironmentService : IEnvironmentService
{
    private readonly AdminDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public EnvironmentService(AdminDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<EnvironmentDto>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Environments
            .AsNoTracking()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Code)
            .Select(e => new EnvironmentDto(e.Id, e.Code, e.DisplayName, e.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<EnvironmentDto> CreateAsync(CreateEnvironmentRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        Validate(req.Code, req.DisplayName);
        var clash = await _db.Environments.AnyAsync(e => e.Code == req.Code, ct);
        if (clash) throw new FlowLifecycleException($"environment code '{req.Code}' already in use");
        var row = new EnvEntity
        {
            Id = Guid.NewGuid(),
            Code = req.Code,
            DisplayName = req.DisplayName,
            SortOrder = req.SortOrder,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
        };
        _db.Environments.Add(row);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("env_created", "environment", row.Id.ToString(), actorUserId, null,
            after: new { row.Code, row.DisplayName, row.SortOrder }, ct: ct);
        return new EnvironmentDto(row.Id, row.Code, row.DisplayName, row.SortOrder);
    }

    public async Task<EnvironmentDto> UpdateAsync(Guid id, UpdateEnvironmentRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await _db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new FlowLifecycleException($"environment {id} not found");
        var before = new { row.Code, row.DisplayName, row.SortOrder };
        if (req.Code is not null && req.Code != row.Code)
        {
            Validate(req.Code, req.DisplayName ?? row.DisplayName);
            var clash = await _db.Environments.AnyAsync(e => e.Code == req.Code && e.Id != id, ct);
            if (clash) throw new FlowLifecycleException($"environment code '{req.Code}' already in use");
            row.Code = req.Code;
        }
        if (req.DisplayName is not null) row.DisplayName = req.DisplayName;
        if (req.SortOrder.HasValue) row.SortOrder = req.SortOrder.Value;
        row.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("env_updated", "environment", row.Id.ToString(), actorUserId, null,
            before: before, after: new { row.Code, row.DisplayName, row.SortOrder }, ct: ct);
        return new EnvironmentDto(row.Id, row.Code, row.DisplayName, row.SortOrder);
    }

    public async Task DeleteAsync(Guid id, Guid? actorUserId, CancellationToken ct = default)
    {
        var row = await _db.Environments.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new FlowLifecycleException($"environment {id} not found");
        row.DeletedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("env_deleted", "environment", row.Id.ToString(), actorUserId, null,
            before: new { row.Code, row.DisplayName }, ct: ct);
    }

    private static void Validate(string code, string displayName)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new FlowLifecycleException("code required");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z0-9_-]+$"))
            throw new FlowLifecycleException("code must be lowercase ascii, digits, hyphen or underscore");
        if (string.IsNullOrWhiteSpace(displayName)) throw new FlowLifecycleException("displayName required");
    }
}

using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Common.Abstractions;
using Bpm.Admin.Application.Flows;
using Bpm.Admin.Domain.Flows;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Persistence.Flows;

public sealed class FlowDeploymentService : IFlowDeploymentService
{
    private readonly AdminDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public FlowDeploymentService(AdminDbContext db, IClock clock, IAuditLogger audit)
    {
        _db = db;
        _clock = clock;
        _audit = audit;
    }

    public async Task<IReadOnlyList<FlowDeploymentDto>> ListAsync(Guid flowId, CancellationToken ct = default)
    {
        var envs = await _db.Environments
            .AsNoTracking()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Code)
            .ToListAsync(ct);
        var existing = await _db.FlowDeployments
            .AsNoTracking()
            .Where(d => d.FlowId == flowId)
            .ToListAsync(ct);
        var byEnv = existing.ToDictionary(d => d.EnvironmentId, d => d);
        return envs.Select(e =>
        {
            if (byEnv.TryGetValue(e.Id, out var row))
            {
                return new FlowDeploymentDto(
                    row.Id, flowId, e.Id, e.Code, e.DisplayName, e.SortOrder,
                    row.Status, row.DeployedAt, row.DeployedByUserId, row.Notes);
            }
            return new FlowDeploymentDto(
                null, flowId, e.Id, e.Code, e.DisplayName, e.SortOrder,
                FlowDeploymentStatus.NotDeployed, null, null, null);
        }).ToList();
    }

    public async Task<FlowDeploymentDto> SetStatusAsync(SetFlowDeploymentRequest req, Guid? actorUserId, CancellationToken ct = default)
    {
        var flowExists = await _db.Flows.AnyAsync(f => f.Id == req.FlowId, ct);
        if (!flowExists) throw new FlowLifecycleException($"flow {req.FlowId} not found");
        var env = await _db.Environments.FirstOrDefaultAsync(e => e.Id == req.EnvironmentId, ct)
            ?? throw new FlowLifecycleException($"environment {req.EnvironmentId} not found");

        var row = await _db.FlowDeployments
            .FirstOrDefaultAsync(d => d.FlowId == req.FlowId && d.EnvironmentId == req.EnvironmentId, ct);
        var before = row is null
            ? new { Status = FlowDeploymentStatus.NotDeployed, DeployedAt = (DateTime?)null }
            : new { row.Status, row.DeployedAt };

        if (row is null)
        {
            row = new FlowDeployment
            {
                Id = Guid.NewGuid(),
                FlowId = req.FlowId,
                EnvironmentId = req.EnvironmentId,
                Status = req.Status,
                DeployedAt = req.Status == FlowDeploymentStatus.Deployed ? _clock.UtcNow : null,
                DeployedByUserId = req.Status == FlowDeploymentStatus.Deployed ? actorUserId : null,
                Notes = req.Notes,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
            };
            _db.FlowDeployments.Add(row);
        }
        else
        {
            row.Status = req.Status;
            row.DeployedAt = req.Status == FlowDeploymentStatus.Deployed ? _clock.UtcNow : null;
            row.DeployedByUserId = req.Status == FlowDeploymentStatus.Deployed ? actorUserId : null;
            row.Notes = req.Notes ?? row.Notes;
            row.UpdatedAt = _clock.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            actionType: req.Status == FlowDeploymentStatus.Deployed ? "flow_deployed" : "flow_undeployed",
            targetType: "flow_deployment",
            targetId: $"{req.FlowId}:{env.Code}",
            actorUserId: actorUserId,
            actorPrincipalId: null,
            before: before,
            after: new { row.Status, row.DeployedAt },
            ct: ct);

        return new FlowDeploymentDto(
            row.Id, req.FlowId, env.Id, env.Code, env.DisplayName, env.SortOrder,
            row.Status, row.DeployedAt, row.DeployedByUserId, row.Notes);
    }
}

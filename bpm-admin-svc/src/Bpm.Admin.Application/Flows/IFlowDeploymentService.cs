using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

/// <summary>
/// Per-flow deployment status board. List returns one entry per
/// configured environment, joined with the flow's existing record
/// (NotDeployed when no row exists yet so admin sees a complete grid).
/// </summary>
public interface IFlowDeploymentService
{
    Task<IReadOnlyList<FlowDeploymentDto>> ListAsync(Guid flowId, CancellationToken ct = default);

    Task<FlowDeploymentDto> SetStatusAsync(SetFlowDeploymentRequest req, Guid? actorUserId, CancellationToken ct = default);
}

public record FlowDeploymentDto(
    Guid? Id,
    Guid FlowId,
    Guid EnvironmentId,
    string EnvironmentCode,
    string EnvironmentDisplayName,
    int EnvironmentSortOrder,
    FlowDeploymentStatus Status,
    DateTime? DeployedAt,
    Guid? DeployedByUserId,
    string? Notes);

public record SetFlowDeploymentRequest(
    Guid FlowId,
    Guid EnvironmentId,
    FlowDeploymentStatus Status,
    string? Notes);

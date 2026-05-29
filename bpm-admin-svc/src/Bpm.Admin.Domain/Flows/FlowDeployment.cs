using System.Text.Json.Serialization;

namespace Bpm.Admin.Domain.Flows;

/// <summary>
/// One row per (FlowId, EnvironmentId) — tracks whether the operator
/// has marked the flow as deployed to that environment. Pure
/// bookkeeping in POC; no automation behind the flag. Repeated
/// Deploy → Undeploy → Deploy cycles update the same row + audit
/// trail, no history table.
/// </summary>
public class FlowDeployment
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Guid EnvironmentId { get; set; }
    public FlowDeploymentStatus Status { get; set; }
    public DateTime? DeployedAt { get; set; }
    public Guid? DeployedByUserId { get; set; }
    /// <summary>Free-text — e.g. PR link, ticket id, "smoke ran 2026-05-29".</summary>
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowDeploymentStatus
{
    NotDeployed = 0,
    Deployed = 1,
}

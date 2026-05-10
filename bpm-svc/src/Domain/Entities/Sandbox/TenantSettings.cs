using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Sandbox;

// Single-row table for the POC's "default" tenant. When add-multi-tenancy lands,
// this becomes per-tenant rows or merges into a Tenant entity.
public sealed class TenantSettings : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";
    public bool SandboxMode { get; set; }
    public string? SandboxConfigJson { get; set; }
    public DateTime? SandboxLastToggledAt { get; set; }
    public Guid? SandboxLastToggledByUserId { get; set; }
}

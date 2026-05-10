using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Sandbox;

public sealed class SandboxRedirect : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";
    public SandboxChannel Channel { get; set; }
    public SandboxAction Action { get; set; }
    public string OriginalTargetsJson { get; set; } = "[]";
    public string RedirectedTargetsJson { get; set; } = "[]";
    public string? SampleSubject { get; set; }
    public DateTime DispatchedAt { get; set; }
}

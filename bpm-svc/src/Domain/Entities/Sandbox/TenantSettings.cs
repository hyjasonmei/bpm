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

    // PR-J3: sandbox-only clock offset (seconds). Kept as a typed column so
    // /api/sandbox/clock/advance can do an atomic +N update without rewriting
    // the SandboxConfigJson blob. Real time + offset = sandbox time.
    public long SandboxClockOffsetSeconds { get; set; } = 0;

    // White-label branding, set from admin's Site Setting → Branding tab and
    // read by bpm-ui (header / login / favicon / page title). Images are kept
    // as small base64 data-URIs — a per-customer logo / favicon is a few KB,
    // so a TEXT column avoids a blob-storage round-trip across services.
    public string? SystemName { get; set; }
    public string? LogoDataUri { get; set; }
    public string? FaviconDataUri { get; set; }
}

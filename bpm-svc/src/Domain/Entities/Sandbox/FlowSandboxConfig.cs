using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Sandbox;

/// <summary>
/// Per-flow sandbox override. The global <c>TenantSettings.SandboxMode</c>
/// switches every flow into sandbox at once; this row lets a single flow be
/// put in sandbox independently (e.g. flow B is still in UAT while flow A is
/// already live in prod).
/// </summary>
/// <remarks>
/// Effective rule (see <c>IFlowSandboxConfigService.IsCaptureEffectiveAsync</c>):
/// a flow's notifications are captured when global SandboxMode is on
/// <em>OR</em> this row's <see cref="CaptureEnabled"/> is true. One row per
/// (TenantCode, FlowCode); absent row ⇒ per-flow off.
/// </remarks>
public sealed class FlowSandboxConfig : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantCode { get; set; } = "default";

    /// <summary>Flow code, upper-cased — matches the <c>&lt;CODE&gt;_V&lt;N&gt;_Case</c> prefix.</summary>
    public string FlowCode { get; set; } = string.Empty;

    /// <summary>Per-flow mail/notification capture toggle.</summary>
    public bool CaptureEnabled { get; set; }
}

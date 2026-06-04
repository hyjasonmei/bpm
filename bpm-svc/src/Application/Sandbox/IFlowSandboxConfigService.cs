namespace Bpm.Application.Sandbox;

/// <summary>
/// Per-flow sandbox scope. Lists the deployed flows with their per-flow
/// capture toggle, flips a single flow's toggle, and answers the effective
/// question the capture dispatcher asks on every notification:
/// "should this flow's mail be captured right now?".
/// </summary>
public interface IFlowSandboxConfigService
{
    /// <summary>Every deployed flow (auto-discovered) + its per-flow toggle.</summary>
    Task<IReadOnlyList<FlowSandboxStateDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Upsert one flow's per-flow capture toggle.</summary>
    Task<FlowSandboxStateDto> SetCaptureAsync(string flowCode, bool enabled, CancellationToken ct = default);

    /// <summary>Effective capture = global SandboxMode OR this flow's toggle.</summary>
    Task<bool> IsCaptureEffectiveAsync(string flowCode, CancellationToken ct = default);
}

/// <param name="CaptureEnabled">The flow's own per-flow toggle (independent of global).</param>
public sealed record FlowSandboxStateDto(string FlowCode, string DisplayName, bool CaptureEnabled);

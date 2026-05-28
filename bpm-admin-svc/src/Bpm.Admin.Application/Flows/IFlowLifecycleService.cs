using Bpm.Admin.Domain.Flows;

namespace Bpm.Admin.Application.Flows;

public class FlowLifecycleException : Exception
{
    public FlowLifecycleException(string message) : base(message) { }
}

public interface IFlowLifecycleService
{
    Task<Flow> CreateDraftAsync(string flowCode, string displayName, string? specJson, Guid? actorUserId, CancellationToken ct = default);

    Task<Flow> UpdateSpecAsync(Guid flowId, string specJson, string? flowCode, string? displayName, Guid? actorUserId, CancellationToken ct = default);

    Task<Flow> SubmitAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CancelAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> ResumeAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> CloneVersionAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    Task<Flow> OnHoldFromChefAsync(Guid flowId, string question, CancellationToken ct = default);
    Task SoftDeleteDraftAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    /// <summary>Approved → Retired. Stops new case instances from
    /// being launched against this flow version.</summary>
    Task<Flow> RetireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
    /// <summary>Retired → Approved. Brings a retired version back
    /// online (rare; usually you'd clone-as-new-version instead).</summary>
    Task<Flow> UnretireAsync(Guid flowId, Guid? actorUserId, CancellationToken ct = default);
}

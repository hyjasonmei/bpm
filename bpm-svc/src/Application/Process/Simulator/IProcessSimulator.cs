using System.Text.Json;
using Bpm.Application.Spec.Bundle;

namespace Bpm.Application.Process.Simulator;

/// <summary>
/// Process Simulator (PR-K3 §4.1) — drives a flow forward against a chosen
/// flow code + form payload without persisting anything to the live database.
///
/// <para>
/// The simulator is a Designer-time companion to the Flow Library: an admin
/// authoring or auditing a spec can press "Simulate" and watch the runtime's
/// own engine walk the flow against a sample input, surfacing the trace,
/// resolved approvers, gateway decisions, and the notifications that
/// <em>would</em> have fired in production. No <see cref="Bpm.Domain.Entities.Process.ProcessInstance"/>
/// row, <see cref="Bpm.Domain.Entities.Process.ProcessTask"/> row,
/// <see cref="Bpm.Domain.Entities.Process.TaskHistory"/> row, or
/// <see cref="Bpm.Domain.Entities.Sandbox.SandboxCapturedMessage"/> row
/// survives the call — every side-effect is rolled back / cleaned up before
/// the result returns.
/// </para>
///
/// <para>
/// The reference implementation toggles sandbox mode on for the duration of
/// the simulation so the existing
/// <c>SandboxCapturingNotificationDispatcher</c> writes capture rows the
/// simulator can surface as <see cref="SimulationNotification"/>s, and
/// restores the prior sandbox state in a finally so the call is idempotent
/// even if the host was never in sandbox.
/// </para>
/// </summary>
public interface IProcessSimulator
{
    Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default);
}

/// <summary>
/// Inputs for one simulation. <see cref="SampleOrg"/> is reserved for a
/// future enhancement (a v1 simulation runs against the existing live org
/// chart — the wizard's sampleOrg-snapshot path lands when the simulator is
/// invoked from a draft bundle, not from a live flow code).
/// </summary>
public sealed record SimulationRequest(
    string FlowCode,
    JsonElement FormData,
    SampleOrgSnapshot? SampleOrg = null,
    Guid? InitiatorUserId = null);

public sealed record SimulationResult(
    bool Success,
    string? Error,
    IReadOnlyList<SimulationStep> Trace,
    IReadOnlyList<SimulationNotification> Notifications,
    JsonElement? FinalFormData,
    string? FinalStatus);

/// <summary>
/// One row in the simulator's trace. <see cref="Outcome"/> uses a small
/// vocabulary so the UI can colour-code consistently:
///
/// <list type="bullet">
///   <item><description><c>spawned</c> — a task was created and is still
///   open at simulation end (the simulator hit the
///   <c>MaxStepsPerSimulation</c> guard or the flow legitimately stopped at
///   this node).</description></item>
///   <item><description><c>auto-advanced</c> — a synthetic node visited by
///   the runtime without spawning a task (start, gateway, notify, end).</description></item>
///   <item><description><c>completed</c> — a userTask / approval task that
///   the simulator auto-submitted on the user's behalf.</description></item>
///   <item><description><c>errored</c> — submission failed; the trace stops
///   here.</description></item>
/// </list>
/// </summary>
public sealed record SimulationStep(
    string NodeId,
    string NodeKind,
    string Outcome,
    Guid? ResolvedAssigneeUserId,
    string? AssigneeName,
    string? Decision,
    string? Notes);

public sealed record SimulationNotification(
    string NotificationId,
    string Trigger,
    string Subject,
    IReadOnlyList<string> Recipients);

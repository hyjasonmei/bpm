using Bpm.Application.Process.Runtime.Commands;

namespace Bpm.Application.Process.Runtime;

/// <summary>
/// Single entry point for state-mutating runtime operations on a
/// <c>ProcessInstance</c>. See design.md §1 for why this is one service.
///
/// Every method opens its own EF transaction, writes one or more
/// <c>TaskHistory</c> rows alongside the state mutation, and dispatches any
/// matching notifications inside the same SaveChanges.
/// </summary>
public interface IProcessRuntime
{
    Task<StartInstanceResult> StartInstanceAsync(StartInstanceCommand cmd, CancellationToken ct = default);

    /// <summary>
    /// Inline-spec overload used by the Spec Bundle reproducibility runner
    /// (PR-I4). Behaves exactly like <see cref="StartInstanceAsync(StartInstanceCommand, CancellationToken)"/>
    /// except the spec text comes from <paramref name="inlineSpecJson"/>
    /// rather than <see cref="Bpm.Application.Spec.ISpecLoader"/>. Lets
    /// the repro runner replay a bundle's <c>spec.json</c> without
    /// registering it on the live spec store.
    /// </summary>
    Task<StartInstanceResult> StartInstanceAsync(StartInstanceCommand cmd, string inlineSpecJson, CancellationToken ct = default);

    Task SubmitTaskAsync(SubmitTaskCommand cmd, CancellationToken ct = default);
    Task ReturnTaskAsync(ReturnTaskCommand cmd, CancellationToken ct = default);
    Task ClaimTaskAsync(ClaimTaskCommand cmd, CancellationToken ct = default);
    Task CancelInstanceAsync(CancelInstanceCommand cmd, CancellationToken ct = default);
}

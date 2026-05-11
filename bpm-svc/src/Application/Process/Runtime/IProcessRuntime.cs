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
    Task SubmitTaskAsync(SubmitTaskCommand cmd, CancellationToken ct = default);
    Task ReturnTaskAsync(ReturnTaskCommand cmd, CancellationToken ct = default);
    Task ClaimTaskAsync(ClaimTaskCommand cmd, CancellationToken ct = default);
    Task CancelInstanceAsync(CancelInstanceCommand cmd, CancellationToken ct = default);
}

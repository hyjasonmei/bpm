namespace Bpm.Application.Common.Abstractions;

/// <summary>
/// PR-J3: opt-in seam for sandbox clock advance to wake scheduled workers
/// (SLA timers, webhook dispatch, etc.) immediately instead of letting them
/// fire on their normal cadence. Default implementation is a no-op; the
/// SLA-timer and webhook-dispatch proposals will register richer impls.
/// </summary>
public interface IScheduledJobKicker
{
    Task KickAsync(CancellationToken ct = default);
}

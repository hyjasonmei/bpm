using Bpm.Application.Common.Abstractions;

namespace Bpm.Persistence.Common;

/// <summary>
/// Default no-op implementation registered by <c>AddPersistence</c>. Future
/// workers (SLA timer, webhook dispatch) replace this with a richer
/// implementation that actually pulses the worker queues.
/// </summary>
public sealed class NoOpScheduledJobKicker : IScheduledJobKicker
{
    public Task KickAsync(CancellationToken ct = default) => Task.CompletedTask;
}

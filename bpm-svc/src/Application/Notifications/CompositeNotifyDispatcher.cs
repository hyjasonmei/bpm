namespace Bpm.Application.Notifications;

/// <summary>
/// Fans a single dispatched notification out to several
/// <see cref="INotifyDispatcher"/> sinks (e.g. the dev file log AND the
/// in-app bell store). Constructed with explicit inner dispatchers at
/// registration time — never resolves <c>IEnumerable&lt;INotifyDispatcher&gt;</c>
/// (that would include itself and recurse). A sink that throws does not
/// stop the others; the first error is rethrown after all have run.
/// </summary>
public sealed class CompositeNotifyDispatcher(params INotifyDispatcher[] inner) : INotifyDispatcher
{
    private readonly INotifyDispatcher[] _inner = inner;

    public async Task DispatchAsync(NotifyMessage message, CancellationToken ct = default)
    {
        Exception? first = null;
        foreach (var d in _inner)
        {
            try
            {
                await d.DispatchAsync(message, ct);
            }
            catch (Exception ex)
            {
                first ??= ex;
            }
        }
        if (first is not null) throw first;
    }
}

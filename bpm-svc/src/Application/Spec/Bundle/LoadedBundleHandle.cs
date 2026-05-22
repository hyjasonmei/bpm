namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Handle returned by <see cref="IBundleRuntimeLoader.LoadAsync"/>: a
/// scoped, disposable view over the seed-data + spec a bundle loaded
/// into. <see cref="DisposeAsync"/> tears down the scratch tenant so
/// each repro run leaves no side-effects in the host DB.
///
/// <para>
/// <see cref="ScratchTenantCode"/> always starts with <c>"repro-"</c> —
/// the loader's defensive cleanup refuses to delete data outside that
/// namespace. Tests that fake-construct a handle pointing at <c>default</c>
/// expect dispose to no-op / throw, never trash production rows.
/// </para>
/// </summary>
public sealed class LoadedBundleHandle : IAsyncDisposable
{
    public required string ScratchTenantCode { get; init; }
    public required string RegisteredSpecCode { get; init; }
    public required int SeededUserCount { get; init; }

    /// <summary>Raw spec.json text — handed inline to the runtime so it doesn't go through ISpecLoader.</summary>
    public required string SpecJson { get; init; }

    /// <summary>Initiator user id used by the repro runner — picked from the sample-org.</summary>
    public required Guid InitiatorUserId { get; init; }

    /// <summary>Cleanup callback supplied by the loader; invoked exactly once at dispose.</summary>
    public Func<ValueTask>? OnDispose { get; init; }

    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (OnDispose is not null) await OnDispose();
    }
}

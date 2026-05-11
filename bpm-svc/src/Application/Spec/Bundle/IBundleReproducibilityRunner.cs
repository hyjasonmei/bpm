namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Drives the live process runtime against each bundled
/// <see cref="TestCaseSnapshot"/> using the scratch context provided by
/// <see cref="IBundleRuntimeLoader"/>. Produces a self-contained
/// <see cref="ReproReport"/> the Flow Library UI can render — no need
/// for the caller to know how the runtime is wired.
/// </summary>
public interface IBundleReproducibilityRunner
{
    Task<ReproReport> RunAsync(
        LoadedBundleHandle handle,
        IReadOnlyList<TestCaseSnapshot> cases,
        CancellationToken ct = default);
}

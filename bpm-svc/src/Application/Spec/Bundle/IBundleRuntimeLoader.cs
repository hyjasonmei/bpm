namespace Bpm.Application.Spec.Bundle;

/// <summary>
/// Loads a parsed bundle into a scratch runtime context — seeding the
/// bundled sample-org under an isolated tenant code so the
/// reproducibility runner can drive the live <c>IProcessRuntime</c>
/// against the bundle's own users / roles / groups without polluting the
/// host tenant.
///
/// <para>
/// The returned <see cref="LoadedBundleHandle"/> is disposable: dispose
/// removes every scratch row the loader inserted. Idempotent — a second
/// call with the same parsed bundle short-circuits to the existing
/// scratch tenant rather than re-seeding (so retries during a flaky
/// repro run don't double-write).
/// </para>
/// </summary>
public interface IBundleRuntimeLoader
{
    Task<LoadedBundleHandle> LoadAsync(ParsedBundle b, CancellationToken ct = default);
}

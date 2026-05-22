namespace Bpm.Application.Spec;

/// <summary>
/// Loads the raw <c>spec.json</c> text for a given (tenant, spec_code) pair.
/// Returns <c>null</c> when no spec is found. The runtime takes the returned
/// text verbatim and stores it as the <c>ProcessInstance.SpecSnapshotJson</c>.
/// </summary>
public interface ISpecLoader
{
    Task<string?> LoadAsync(string tenantCode, string specCode, CancellationToken ct = default);
}

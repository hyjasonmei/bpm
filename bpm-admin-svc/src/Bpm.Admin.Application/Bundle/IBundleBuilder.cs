namespace Bpm.Admin.Application.Bundle;

/// <summary>
/// Packages a draft spec + supporting artifacts into the canonical
/// bundle <c>.zip</c> bytes. Pure: no DB, no filesystem; callers own
/// persistence and HTTP delivery.
/// </summary>
public interface IBundleBuilder
{
    Task<byte[]> BuildAsync(BundleBuildRequest req, CancellationToken ct = default);
}

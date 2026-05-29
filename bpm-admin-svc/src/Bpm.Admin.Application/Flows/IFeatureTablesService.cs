namespace Bpm.Admin.Application.Flows;

/// <summary>
/// Site Setting → Feature Tables surface. Scans the shared SQLite DB
/// for chef-cooked tables (naming convention <c>&lt;CODE&gt;_V&lt;N&gt;_*</c>),
/// cross-references with admin's Flows registry, and offers archive
/// (rename-with-hash) / restore actions.
/// </summary>
public interface IFeatureTablesService
{
    Task<IReadOnlyList<FeatureTableGroupDto>> ScanAsync(CancellationToken ct = default);

    Task<FeatureTableGroupDto> ArchiveAsync(ArchiveFeatureRequest req, Guid? actorUserId, CancellationToken ct = default);

    Task<FeatureTableGroupDto> RestoreAsync(RestoreFeatureRequest req, Guid? actorUserId, CancellationToken ct = default);
}

public record FeatureTableGroupDto(
    string FlowCode,
    int Version,
    string Status,                                   // 'Linked' | 'Orphan' | 'Archived' | 'Dangling'
    Guid? FlowId,
    string? FlowDisplayName,
    string? FlowState,
    DateTime? ArchivedAt,
    IReadOnlyList<string> TableNames,                // live tables (current name)
    IReadOnlyList<string> ArchivedTableNames);       // suffixed names when Status == 'Archived'

public record ArchiveFeatureRequest(string FlowCode, int Version, Guid? FlowId);
public record RestoreFeatureRequest(string FlowCode, int Version);

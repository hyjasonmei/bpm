using Bpm.Domain.Features.FAD.V1;

namespace Bpm.Application.Features.FAD.V1;

/// <summary>Per-flow data access port for the FAD V1 case table.</summary>
public interface IFAD_V1_CaseStore
{
    void Add(FAD_V1_Case @case);
    Task<FAD_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<FAD_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<FAD_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

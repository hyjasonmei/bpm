using Bpm.Domain.Features.FAP.V1;

namespace Bpm.Application.Features.FAP.V1;

/// <summary>Per-flow data access port for the FAP V1 case table.</summary>
public interface IFAP_V1_CaseStore
{
    void Add(FAP_V1_Case @case);
    Task<FAP_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<FAP_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<FAP_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

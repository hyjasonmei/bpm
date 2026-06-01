using Bpm.Domain.Features.ETM.V1;

namespace Bpm.Application.Features.ETM.V1;

/// <summary>Per-flow data access port for the ETM V1 case table.</summary>
public interface IETM_V1_CaseStore
{
    void Add(ETM_V1_Case @case);
    Task<ETM_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);
    Task<IReadOnlyList<ETM_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);
    Task<IReadOnlyList<ETM_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

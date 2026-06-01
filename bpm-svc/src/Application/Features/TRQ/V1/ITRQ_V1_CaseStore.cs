using Bpm.Domain.Features.TRQ.V1;

namespace Bpm.Application.Features.TRQ.V1;

/// <summary>
/// Per-flow data access port for the TRQ V1 case table. Application
/// can't reference Persistence (cyclic dep), so chef ships this
/// interface alongside the state machine and the EF impl lives in
/// <c>Persistence/Features/TRQ/V1/</c>.
/// </summary>
public interface ITRQ_V1_CaseStore
{
    void Add(TRQ_V1_Case @case);

    Task<TRQ_V1_Case?> FindByIdAsync(Guid caseId, CancellationToken ct = default);

    Task<IReadOnlyList<TRQ_V1_Case>> FindMineAsync(Guid submitterUserId, CancellationToken ct = default);

    Task<IReadOnlyList<TRQ_V1_Case>> FindPendingAsync(Guid assigneeUserId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

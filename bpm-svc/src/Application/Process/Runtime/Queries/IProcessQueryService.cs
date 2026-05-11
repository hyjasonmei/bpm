using Bpm.Application.Process.Runtime.Dtos;

namespace Bpm.Application.Process.Runtime.Queries;

/// <summary>
/// Read-side queries for the process runtime. Kept separate from
/// <see cref="IProcessRuntime"/> so reads don't drag the full transaction +
/// hook orchestration along.
/// </summary>
public interface IProcessQueryService
{
    /// <summary>Returns the instance + open tasks. Throws NotFound if missing,
    /// Forbidden if the requester isn't the initiator (v1 rule).</summary>
    Task<ProcessInstanceDto> GetInstanceAsync(Guid instanceId, Guid requesterUserId, CancellationToken ct = default);

    /// <summary>Cursor-paginated history; cursor encodes <c>CreatedAt</c> of
    /// the last seen row.</summary>
    Task<HistoryPage> GetHistoryPageAsync(Guid instanceId, Guid requesterUserId, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Tasks where the requester is the actual assignee, filtered by
    /// status. <c>open</c> = Pending/InProgress, <c>completed</c> =
    /// Completed/Cancelled, <c>all</c> = no filter.</summary>
    Task<IReadOnlyList<ProcessTaskDto>> GetMineAsync(Guid requesterUserId, string status, int limit, CancellationToken ct = default);

    /// <summary>Single task with merged form snapshot. Auth: assignee OR
    /// initiator.</summary>
    Task<TaskWithFormDto> GetTaskAsync(Guid taskId, Guid requesterUserId, CancellationToken ct = default);

    /// <summary>
    /// Admin-wide list of active (Running / Errored) process instances with
    /// the data the LiveCases monitor needs to render its row: initiator,
    /// open-task fan-out, breach flag, and a single-assignee shortcut when
    /// there's exactly one open task. No requester check — admin-role gate
    /// at the controller layer is the auth boundary.
    /// </summary>
    Task<IReadOnlyList<ActiveCaseDto>> GetActiveCasesAsync(
        string? specCode = null,
        int? maxAgeDays = null,
        bool breachOnly = false,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Admin-only single-case bundle: instance header + open tasks +
    /// most recent <paramref name="historyLimit"/> history entries (newest
    /// first). Skips the initiator-only check that <see cref="GetInstanceAsync"/>
    /// applies — admins see everyone's cases.
    /// </summary>
    Task<LiveCaseDetailDto> GetCaseDetailAsync(
        Guid instanceId,
        int historyLimit = 20,
        CancellationToken ct = default);

    /// <summary>
    /// PR-K5 §7.1 — terminal (Completed / Cancelled) instances for the
    /// CompletedCases admin table. Cursor pagination uses the same
    /// <c>{TerminalAt}|{Id}</c> composite cursor pattern as
    /// <see cref="GetHistoryPageAsync"/> so concurrent terminations sharing
    /// a millisecond don't drop or duplicate rows.
    /// </summary>
    /// <param name="status">
    /// "completed" → only <see cref="InstanceStatus.Completed"/>;
    /// "cancelled" → only <see cref="InstanceStatus.Cancelled"/>;
    /// any other value (incl. null) → both terminal kinds.
    /// </param>
    Task<CompletedCasesPage> GetCompletedCasesAsync(
        string? specCode = null,
        DateTime? completedAfter = null,
        string? status = null,
        int limit = 100,
        string? cursor = null,
        CancellationToken ct = default);
}

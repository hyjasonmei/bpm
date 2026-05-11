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
}

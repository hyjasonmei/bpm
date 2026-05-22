namespace Bpm.Application.Sandbox;

/// <summary>
/// PR-J4 §5: sandbox-only state reset. Deletes process instances, tasks,
/// history rows, and captured messages so a tester can re-run a UAT scenario
/// from a clean slate without touching specs / org / bundle data. Refuses to
/// run when sandbox mode is off — the destructive surface is intentionally
/// gated behind the sandbox toggle.
/// </summary>
public interface IResetService
{
    Task<ResetSummary> ResetInstanceAsync(Guid instanceId, Guid actorUserId, CancellationToken ct = default);
    Task<ResetSummary> ResetAllAsync(Guid actorUserId, CancellationToken ct = default);
}

/// <summary>
/// Counts of rows hard-deleted by a single reset call. Returned to the
/// caller so the UI can show "wiped 3 instances / 12 tasks / 47 history /
/// 8 messages" without re-querying.
/// </summary>
public sealed record ResetSummary(
    int InstancesDeleted,
    int TasksDeleted,
    int HistoryRowsDeleted,
    int CapturedMessagesDeleted);

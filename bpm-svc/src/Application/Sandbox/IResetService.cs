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

    /// <summary>
    /// Clear a single Model-B flow's cases (its <c>&lt;CODE&gt;_V*_Case</c> table[s])
    /// plus that flow's captured messages — for re-running one flow's UAT
    /// without wiping the rest. Sandbox-gated like the others.
    /// </summary>
    Task<ResetSummary> ResetFlowAsync(string flowCode, Guid actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Full demo "factory reset": unconditionally hard-deletes ALL runtime
    /// data — every Model-B case table, process instances / tasks / history,
    /// captured mail, user notifications, dispatch + actor-resolution audits,
    /// doctor logs, impersonation sessions, attendance punches, HR-flow rows —
    /// and clears the sandbox clock offset. Unlike <see cref="ResetAllAsync"/>
    /// this is NOT gated behind the sandbox toggle and is not tenant-scoped:
    /// it backs the admin-ui Reset tab's "wipe DB back to seed-init" button.
    /// Identity (the Shared* tables) is owned by admin-svc and reseeded there.
    /// </summary>
    Task<ResetSummary> FactoryResetAsync(CancellationToken ct = default);
}

/// <summary>
/// Counts of rows hard-deleted by a single reset call. Returned to the
/// caller so the UI can show "wiped 3 instances / 12 tasks / 47 history /
/// 8 messages / 5 cases" without re-querying. <c>CasesDeleted</c> covers the
/// Model-B <c>&lt;CODE&gt;_V*_Case</c> rows (the others are Model-A runtime tables).
/// </summary>
public sealed record ResetSummary(
    int InstancesDeleted,
    int TasksDeleted,
    int HistoryRowsDeleted,
    int CapturedMessagesDeleted,
    int CasesDeleted = 0);

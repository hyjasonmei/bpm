namespace Bpm.Domain.Features.WFH.V3;

/// <summary>
/// Workflow state for a WFH (居家辦公申請 / Work-From-Home) V3 case.
///
/// V3 differs from V2 only in the gateway threshold: the senior
/// (上級主管) approval kicks in at <b>≥ 30 consecutive days</b> instead of
/// V2's ≥ 15 (V1 was &gt; 7). The state graph is otherwise identical.
///
/// Graph (spec nodes start_1 / task_apply / approval_manager /
/// gateway_days / approval_senior / end_*):
/// <code>
///   PendingManager ──approve, days &lt; 30──► Completed
///        │                │
///        │             approve, days &gt;= 30
///        │                ▼
///        │           PendingSenior ──approve──► Completed
///        │                │
///      reject           reject
///        ▼                ▼
///   ResubmitRequired ◄────┘  (send-back to submitter; resubmit → PendingManager)
/// </code>
/// Both reject edges loop back to the submit task, so a rejected case
/// lands in <c>ResubmitRequired</c> (non-terminal) rather than a hard
/// reject. <c>Cancelled</c> is the submitter-withdraw terminal.
/// </summary>
public enum WFH_V3_CaseStatus
{
    PendingManager   = 0,
    PendingSenior    = 1,
    ResubmitRequired = 2,
    Completed        = 3,
    Cancelled        = 4,
}

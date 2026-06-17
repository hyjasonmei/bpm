namespace Bpm.Domain.Features.WFH.V1;

/// <summary>
/// Workflow state for a WFH (居家辦公申請 / Work-From-Home) V1 case.
///
/// Graph (spec nodes start_1 / task_apply / approval_manager /
/// gateway_days / approval_senior / end_*):
/// <code>
///   PendingManager ──approve, days &lt;= 7──► Completed
///        │                │
///        │             approve, days &gt; 7
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
public enum WFH_V1_CaseStatus
{
    PendingManager   = 0,
    PendingSenior    = 1,
    ResubmitRequired = 2,
    Completed        = 3,
    Cancelled        = 4,
}

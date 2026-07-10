namespace Bpm.Domain.Features.OVERTIME.V1;

/// <summary>
/// Lifecycle for an OVERTIME V1 (加班申請) case. Mirrors the bundle's BPMN:
/// <c>PendingManager</c> (直屬主管審核) → gateway on 單月累計時數 → either
/// <c>PendingHr</c> (人資複核, when the month's cumulative overtime exceeds the
/// threshold) or straight to <c>Completed</c>. Manager / HR reject are terminal
/// (the spec routes each rejection directly to an end event — no send-back).
/// <c>Cancelled</c> is the baseline submitter-withdraw terminal.
/// </summary>
public enum OVERTIME_V1_CaseStatus
{
    PendingManager    = 0,
    PendingHr         = 1,
    Completed         = 2,
    RejectedByManager = 3,
    RejectedByHr      = 4,
    Cancelled         = 5,
}

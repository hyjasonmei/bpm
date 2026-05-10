namespace Bpm.Domain.Entities.Process;

public enum HistoryEventType
{
    InstanceStarted = 1,
    TaskSpawned = 2,
    TaskClaimed = 3,
    TaskSubmitted = 4,
    TaskReturned = 5,
    ApprovalApproved = 6,
    ApprovalRejected = 7,
    NotificationDispatched = 8,
    NotificationCaptured = 9,
    GatewayEvaluated = 10,
    InstanceCompleted = 11,
    InstanceCancelled = 12,
    DelegationApplied = 13,
    SlaWarning = 14,
    SlaBreached = 15,
}

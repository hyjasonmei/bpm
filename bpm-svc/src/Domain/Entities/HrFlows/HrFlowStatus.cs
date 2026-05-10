namespace Bpm.Domain.Entities.HrFlows;

public enum HrFlowStatus
{
    PendingManager = 1,
    PendingHr = 2,
    Returned = 3,
    Completed = 4,
    Cancelled = 5,
}

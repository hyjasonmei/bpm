namespace Bpm.Persistence.Features.LEAVE.V1;

public enum LEAVE_V1_CaseStatus
{
    PendingManager = 0,
    PendingVp      = 1,
    PendingHr      = 2,
    Completed      = 3,
    Rejected       = 4,
    Cancelled      = 5,
}

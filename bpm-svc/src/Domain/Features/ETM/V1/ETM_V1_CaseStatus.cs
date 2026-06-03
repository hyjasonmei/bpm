namespace Bpm.Domain.Features.ETM.V1;

public enum ETM_V1_CaseStatus
{
    PendingManager   = 0,
    PendingHandover  = 1,
    ResubmitRequired = 2,
    Completed        = 3,
    Cancelled        = 4,
}

namespace Bpm.Domain.Features.FAD.V1;

public enum FAD_V1_CaseStatus
{
    PendingManager   = 0,
    PendingConfirm   = 1,
    ResubmitRequired = 2,
    Completed        = 3,
    Cancelled        = 4,
}

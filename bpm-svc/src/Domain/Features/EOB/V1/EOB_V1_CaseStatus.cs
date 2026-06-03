namespace Bpm.Domain.Features.EOB.V1;

public enum EOB_V1_CaseStatus
{
    PendingManager   = 0,
    PendingSetup     = 1,
    ResubmitRequired = 2,
    Completed        = 3,
    Cancelled        = 4,
}

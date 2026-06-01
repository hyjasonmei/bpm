namespace Bpm.Domain.Features.FAP.V1;

public enum FAP_V1_CaseStatus
{
    PendingManager      = 0,
    PendingVerification = 1,
    ResubmitRequired    = 2,
    Completed           = 3,
}

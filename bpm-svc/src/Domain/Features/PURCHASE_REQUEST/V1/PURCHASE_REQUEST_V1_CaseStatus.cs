namespace Bpm.Domain.Features.PURCHASE_REQUEST.V1;

public enum PURCHASE_REQUEST_V1_CaseStatus
{
    PendingDeptHead   = 0,
    PendingFinance    = 1,
    ResubmitRequired  = 2,
    Completed         = 3,
}

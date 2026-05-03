namespace Bpm.Domain.States;

public enum PurchaseState
{
    Draft = 0,
    PendingManagerApproval = 1,
    PendingFinanceApproval = 2,
    PendingCeoApproval = 3,
    PendingPurchaseExec = 4,
    Completed = 5,
    Rejected = 6,
}

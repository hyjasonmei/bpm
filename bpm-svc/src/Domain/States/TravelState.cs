namespace Bpm.Domain.States;

public enum TravelState
{
    Draft = 0,
    PendingManagerApproval = 1,
    PendingVpApproval = 2,
    PendingAdminBook = 3,
    Completed = 4,
    Rejected = 5,
}

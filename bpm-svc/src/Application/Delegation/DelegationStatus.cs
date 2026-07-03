namespace Bpm.Application.Delegation;

/// Acceptance state of a delegation, mirroring admin-svc's DelegationStatus.
/// A delegation only takes effect (grants the delegate authority) when Accepted.
public enum DelegationStatus
{
    Pending  = 0,
    Accepted = 1,
    Declined = 2,
}

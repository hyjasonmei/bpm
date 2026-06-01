namespace Bpm.Domain.Features.TRQ.V1;

/// <summary>
/// Persistent case for the TRQ (Travel Request) V1 flow. Holds the
/// submitter's travel-request payload (itinerary + traveller prefs)
/// plus per-stage workflow state (Status enum, current assignee,
/// manager decision columns). One row per submitted travel request.
///
/// The spec has a single approval node (<c>ap</c> = submitter.manager).
/// Its reject edge loops back to the submit task (<c>req</c>), so a
/// rejected case lands in <c>ResubmitRequired</c> with the assignee
/// reverted to the original submitter; on resubmit it re-enters
/// <c>PendingManager</c>. There is no cancel or revoke action in the
/// spec. POCO only — no EF / service refs.
/// </summary>
public class TRQ_V1_Case
{
    public Guid Id { get; set; }

    // Business data — mirrors the bundle's userTask `req` fields.
    public Guid SubmitterUserId { get; set; }
    public string TravelType { get; set; } = string.Empty;        // Round Trip | One Way
    public string DepartureCity { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public DateOnly DepartDate { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public string ChargeTo { get; set; } = string.Empty;
    public string TravelPurpose { get; set; } = string.Empty;
    public string? PassportName { get; set; }
    public string? SeatPreference { get; set; }                   // Window | Aisle | No preference
    public string? PickupRequired { get; set; }                   // Yes | No

    // Workflow state
    public TRQ_V1_CaseStatus Status { get; set; } = TRQ_V1_CaseStatus.PendingManager;
    public Guid? CurrentAssigneeUserId { get; set; }
    public int RoundCount { get; set; } = 1;

    // Manager (submitter.manager) decision columns. Approved=true/false
    // captures the decision; null = not yet acted. On reject the columns
    // are cleared before the next round so the timeline reflects the
    // latest pass only.
    public Guid? ManagerUserId { get; set; }
    public bool? ManagerApproved { get; set; }
    public string? ManagerComment { get; set; }
    public DateTime? ManagerDecisionAt { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

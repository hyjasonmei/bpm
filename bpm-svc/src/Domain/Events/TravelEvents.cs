using Bpm.Domain.States;

namespace Bpm.Domain.Events;

public abstract record TravelDomainEvent(Guid CaseId, DateTime OccurredAt);

public sealed record TravelSubmitted(
    Guid CaseId, DateTime OccurredAt,
    string ApplicantUserId, string DestinationType, string Destination, decimal EstimatedCost
) : TravelDomainEvent(CaseId, OccurredAt);

public sealed record TravelStepApproved(
    Guid CaseId, DateTime OccurredAt,
    TravelState FromState, TravelState ToState, string ApproverUserId
) : TravelDomainEvent(CaseId, OccurredAt);

public sealed record TravelRejected(
    Guid CaseId, DateTime OccurredAt,
    TravelState FromState, string ApproverUserId, string Reason
) : TravelDomainEvent(CaseId, OccurredAt);

public sealed record TravelBooked(
    Guid CaseId, DateTime OccurredAt,
    string AdminUserId, string TicketRef
) : TravelDomainEvent(CaseId, OccurredAt);

public sealed record TravelCompleted(
    Guid CaseId, DateTime OccurredAt
) : TravelDomainEvent(CaseId, OccurredAt);

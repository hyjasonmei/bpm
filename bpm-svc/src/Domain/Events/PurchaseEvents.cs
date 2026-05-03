using Bpm.Domain.States;

namespace Bpm.Domain.Events;

public abstract record PurchaseDomainEvent(Guid CaseId, DateTime OccurredAt);

public sealed record PurchaseSubmitted(
    Guid CaseId,
    DateTime OccurredAt,
    string ApplicantUserId,
    decimal Amount,
    string Vendor,
    string Category
) : PurchaseDomainEvent(CaseId, OccurredAt);

public sealed record PurchaseStepApproved(
    Guid CaseId,
    DateTime OccurredAt,
    PurchaseState FromState,
    PurchaseState ToState,
    string ApproverUserId
) : PurchaseDomainEvent(CaseId, OccurredAt);

public sealed record PurchaseRejected(
    Guid CaseId,
    DateTime OccurredAt,
    PurchaseState FromState,
    string ApproverUserId,
    string Reason
) : PurchaseDomainEvent(CaseId, OccurredAt);

public sealed record PurchaseExecuted(
    Guid CaseId,
    DateTime OccurredAt,
    string ExecUserId,
    string PoNumber,
    DateOnly ExpectedDelivery
) : PurchaseDomainEvent(CaseId, OccurredAt);

public sealed record PurchaseCompleted(
    Guid CaseId,
    DateTime OccurredAt
) : PurchaseDomainEvent(CaseId, OccurredAt);

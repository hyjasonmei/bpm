namespace Bpm.Api.Features.CONTRACT_REVIEW.V1;

/// <summary>Submit / resubmit payload (task_apply / task_revise form).</summary>
public sealed record CONTRACT_REVIEW_V1_SubmitRequest(
    string CounterpartyName,
    string ContractSubject,
    decimal Amount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? DraftFileId,
    string? Remarks,
    string? RevisionNote);

/// <summary>Approve / reject for a parallel slot OR the legal-manager step.</summary>
public sealed record CONTRACT_REVIEW_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record CONTRACT_REVIEW_V1_SlotView(
    Guid SlotId, string NodeId, string? RoleCode, string State, string? DeciderName, string? Comment, DateTime? At);

public sealed record CONTRACT_REVIEW_V1_ReviewView(
    string PolicyLabel, int Threshold, int ApprovedCount, int Total, IReadOnlyList<CONTRACT_REVIEW_V1_SlotView> Slots);

public sealed record CONTRACT_REVIEW_V1_LegalManagerView(
    Guid? UserId, string? Name, bool? Approved, string? Comment, DateTime? At);

public sealed record CONTRACT_REVIEW_V1_CaseResponse(
    Guid Id,
    string CounterpartyName,
    string ContractSubject,
    decimal Amount,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? DraftFileId,
    string? Remarks,
    string? RevisionNote,
    string Status,
    int CurrentRound,
    Guid SubmitterUserId,
    string? SubmitterName,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt,
    CONTRACT_REVIEW_V1_ReviewView? Review,
    CONTRACT_REVIEW_V1_LegalManagerView? LegalManager);

public sealed record CONTRACT_REVIEW_V1_RowResponse(
    Guid Id, string ContractSubject, string CounterpartyName, string Status,
    DateTime SubmittedAt, DateTime LastActivityAt);

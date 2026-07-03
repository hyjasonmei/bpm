namespace Bpm.Api.Features.CONTRACT_REVIEW.V1;

public sealed record CONTRACT_REVIEW_V1_SubmitRequest(
    string Title, string Counterparty, decimal Amount, string? Currency, Guid? ContractFileId);

public sealed record CONTRACT_REVIEW_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record CONTRACT_REVIEW_V1_SlotView(
    Guid SlotId, string NodeId, string? RoleCode, string State, string? DeciderName, string? Comment, DateTime? At);

public sealed record CONTRACT_REVIEW_V1_ReviewView(
    string PolicyLabel, int Threshold, int ApprovedCount, int Total, IReadOnlyList<CONTRACT_REVIEW_V1_SlotView> Slots);

public sealed record CONTRACT_REVIEW_V1_CaseResponse(
    Guid Id, string Title, string Counterparty, decimal Amount, string Currency, string Status,
    Guid SubmitterUserId, string? SubmitterName, DateTime SubmittedAt, DateTime LastActivityAt,
    CONTRACT_REVIEW_V1_ReviewView? Review);

public sealed record CONTRACT_REVIEW_V1_RowResponse(
    Guid Id, string Title, string Counterparty, string Status, DateTime SubmittedAt, DateTime LastActivityAt);

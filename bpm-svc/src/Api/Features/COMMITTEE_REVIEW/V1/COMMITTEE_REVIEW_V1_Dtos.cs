namespace Bpm.Api.Features.COMMITTEE_REVIEW.V1;

public sealed record COMMITTEE_REVIEW_V1_SubmitRequest(string Title, decimal Amount, string? Currency, string Purpose);

public sealed record COMMITTEE_REVIEW_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record COMMITTEE_REVIEW_V1_SlotView(
    Guid SlotId, string NodeId, string? RoleCode, string State, string? DeciderName, string? Comment, DateTime? At);

public sealed record COMMITTEE_REVIEW_V1_ReviewView(
    string PolicyLabel, int Threshold, int ApprovedCount, int Total, IReadOnlyList<COMMITTEE_REVIEW_V1_SlotView> Slots);

public sealed record COMMITTEE_REVIEW_V1_CaseResponse(
    Guid Id, string Title, decimal Amount, string Currency, string Purpose, string Status,
    Guid SubmitterUserId, string? SubmitterName, DateTime SubmittedAt, DateTime LastActivityAt,
    COMMITTEE_REVIEW_V1_ReviewView? Review);

public sealed record COMMITTEE_REVIEW_V1_RowResponse(
    Guid Id, string Title, string Status, DateTime SubmittedAt, DateTime LastActivityAt);

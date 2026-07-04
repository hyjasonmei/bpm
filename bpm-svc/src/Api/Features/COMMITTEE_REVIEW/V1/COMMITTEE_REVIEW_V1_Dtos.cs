namespace Bpm.Api.Features.COMMITTEE_REVIEW.V1;

/// <summary>Submit / resubmit payload (task_apply / task_revise form).</summary>
public sealed record COMMITTEE_REVIEW_V1_SubmitRequest(
    string CaseTitle,
    string ReviewCategory,
    decimal ApplyAmount,
    string BenefitDescription,
    DateOnly ExecStart,
    DateOnly ExecEnd,
    Guid? AttachmentFileId,
    string? Remarks,
    string? RevisionNote);

/// <summary>Approve / reject for a parallel committee slot OR the CEO step.</summary>
public sealed record COMMITTEE_REVIEW_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record COMMITTEE_REVIEW_V1_SlotView(
    Guid SlotId, string NodeId, string? RoleCode, string State, string? DeciderName, string? Comment, DateTime? At);

public sealed record COMMITTEE_REVIEW_V1_ReviewView(
    string PolicyLabel, int Threshold, int ApprovedCount, int Total, IReadOnlyList<COMMITTEE_REVIEW_V1_SlotView> Slots);

public sealed record COMMITTEE_REVIEW_V1_CeoView(
    Guid? UserId, string? Name, bool? Approved, string? Comment, DateTime? At);

public sealed record COMMITTEE_REVIEW_V1_CaseResponse(
    Guid Id,
    string CaseTitle,
    string ReviewCategory,
    string ReviewCategoryLabel,
    decimal ApplyAmount,
    string BenefitDescription,
    DateOnly ExecStart,
    DateOnly ExecEnd,
    Guid? AttachmentFileId,
    string? Remarks,
    string? RevisionNote,
    string Status,
    int CurrentRound,
    Guid SubmitterUserId,
    string? SubmitterName,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt,
    COMMITTEE_REVIEW_V1_ReviewView? Review,
    COMMITTEE_REVIEW_V1_CeoView? Ceo);

public sealed record COMMITTEE_REVIEW_V1_RowResponse(
    Guid Id, string CaseTitle, string ReviewCategoryLabel, string Status,
    DateTime SubmittedAt, DateTime LastActivityAt);

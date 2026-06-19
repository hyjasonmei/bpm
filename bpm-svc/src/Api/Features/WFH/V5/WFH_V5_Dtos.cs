using Bpm.Application.Features.WFH.V5;
using Bpm.Domain.Features.WFH.V5;

namespace Bpm.Api.Features.WFH.V5;

/// <summary>WFH date range — posted as a nested object by the form (mirrors LEAVE's shape).</summary>
public sealed record WFH_V5_DateRange(DateOnly Start, DateOnly End);

public sealed record WFH_V5_SubmitRequest(
    DateOnly ApplyDate,
    WFH_V5_DateRange DateRange,
    string Reason,
    Guid? AttachmentFileId);

public sealed record WFH_V5_DecisionRequest(bool Approve, string? Comment);

public sealed record WFH_V5_DecisionDto(
    Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record WFH_V5_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    DateOnly ApplyDate,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    string Reason,
    Guid? AttachmentFileId,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    WFH_V5_DecisionDto? ManagerDecision,
    WFH_V5_DecisionDto? SeniorDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record WFH_V5_CaseRowResponse(
    Guid Id,
    int Days,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class WFH_V5_DtoMapping
{
    public static WFH_V5_CaseResponse ToResponse(WFH_V5_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            ApplyDate: c.ApplyDate,
            StartDate: c.StartDate,
            EndDate: c.EndDate,
            Days: c.Days,
            Reason: c.Reason,
            AttachmentFileId: c.AttachmentFileId,
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new WFH_V5_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value),
                c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            SeniorDecision: c.SeniorUserId is null ? null : new WFH_V5_DecisionDto(
                c.SeniorUserId, names.GetValueOrDefault(c.SeniorUserId.Value),
                c.SeniorApproved, c.SeniorComment, c.SeniorDecisionAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);

    public static WFH_V5_WfhService.SubmitInput ToServiceInput(Guid submitterUserId, WFH_V5_SubmitRequest req)
        => new(submitterUserId, req.ApplyDate, req.DateRange.Start, req.DateRange.End, req.Reason, req.AttachmentFileId);
}

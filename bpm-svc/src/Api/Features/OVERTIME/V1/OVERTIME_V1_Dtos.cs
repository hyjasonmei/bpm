using Bpm.Application.Features.OVERTIME.V1;
using Bpm.Domain.Features.OVERTIME.V1;

namespace Bpm.Api.Features.OVERTIME.V1;

public sealed record OVERTIME_V1_SubmitRequest(
    DateOnly OvertimeDate,
    string? StartTime,
    string? EndTime,
    decimal? EstimatedHours,
    string OvertimeReason);

/// <summary>Approve/reject payload for both the manager and HR decision endpoints.
/// Comment is optional (spec's reject actions carry promptComment: "optional").</summary>
public sealed record OVERTIME_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record OVERTIME_V1_DecisionDto(
    Guid? UserId,
    string? DisplayName,
    bool? Approved,
    string? Comment,
    DateTime? DecidedAt);

public sealed record OVERTIME_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    DateOnly OvertimeDate,
    string? StartTime,
    string? EndTime,
    decimal? EstimatedHours,
    string OvertimeReason,
    string Status,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    string? CurrentAssigneeRoleCode,
    decimal? MonthlyHours,
    OVERTIME_V1_DecisionDto? ManagerDecision,
    OVERTIME_V1_DecisionDto? HrDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record OVERTIME_V1_CaseRowResponse(
    Guid Id,
    string Status,
    DateOnly OvertimeDate,
    decimal? EstimatedHours,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class OVERTIME_V1_DtoMapping
{
    public static OVERTIME_V1_OvertimeService.SubmitInput ToServiceInput(
        Guid submitterUserId, OVERTIME_V1_SubmitRequest req)
        => new(
            SubmitterUserId: submitterUserId,
            OvertimeDate: req.OvertimeDate,
            StartTime: req.StartTime,
            EndTime: req.EndTime,
            EstimatedHours: req.EstimatedHours,
            OvertimeReason: req.OvertimeReason ?? string.Empty);

    public static OVERTIME_V1_CaseResponse ToResponse(
        OVERTIME_V1_Case c, IReadOnlyDictionary<Guid, string> displayNames)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: displayNames.GetValueOrDefault(c.SubmitterUserId),
            OvertimeDate: c.OvertimeDate,
            StartTime: c.StartTime,
            EndTime: c.EndTime,
            EstimatedHours: c.EstimatedHours,
            OvertimeReason: c.OvertimeReason,
            Status: c.Status.ToString(),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a
                ? displayNames.GetValueOrDefault(a) : null,
            CurrentAssigneeRoleCode: c.CurrentAssigneeRoleCode,
            MonthlyHours: c.MonthlyHours,
            ManagerDecision: c.ManagerUserId is null ? null : new OVERTIME_V1_DecisionDto(
                UserId: c.ManagerUserId,
                DisplayName: displayNames.GetValueOrDefault(c.ManagerUserId.Value),
                Approved: c.ManagerApproved,
                Comment: c.ManagerComment,
                DecidedAt: c.ManagerDecisionAt),
            HrDecision: c.HrUserId is null ? null : new OVERTIME_V1_DecisionDto(
                UserId: c.HrUserId,
                DisplayName: displayNames.GetValueOrDefault(c.HrUserId.Value),
                Approved: c.HrApproved,
                Comment: c.HrComment,
                DecidedAt: c.HrDecisionAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);
}

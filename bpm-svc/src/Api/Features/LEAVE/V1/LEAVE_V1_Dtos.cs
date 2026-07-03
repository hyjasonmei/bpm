using Bpm.Domain.Features.LEAVE.V1;

namespace Bpm.Api.Features.LEAVE.V1;

public sealed record LEAVE_V1_DateRange(DateOnly Start, DateOnly End);

public sealed record LEAVE_V1_SubmitRequest(
    string LeaveType,
    LEAVE_V1_DateRange DateRange,
    string Reason,
    Guid? CertFileId);

public sealed record LEAVE_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record LEAVE_V1_HrArchiveRequest(string ArchiveNote);

public sealed record LEAVE_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Days,
    string Reason,
    Guid? CertFileId,
    string? ArchiveNote,
    string Status,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    string? CurrentAssigneeRoleCode,
    LEAVE_V1_DecisionDto? ManagerDecision,
    LEAVE_V1_DecisionDto? VpDecision,
    LEAVE_V1_DecisionDto? HrArchive,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record LEAVE_V1_DecisionDto(
    Guid? UserId,
    string? DisplayName,
    bool? Approved,
    string? Comment,
    DateTime? DecidedAt);

public sealed record LEAVE_V1_CaseRowResponse(
    Guid Id,
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal Days,
    string Status,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class LEAVE_V1_DtoMapping
{
    public static LEAVE_V1_CaseResponse ToResponse(
        LEAVE_V1_Case c,
        IReadOnlyDictionary<Guid, string> displayNames)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: displayNames.GetValueOrDefault(c.SubmitterUserId),
            LeaveType: c.LeaveType,
            StartDate: c.StartDate,
            EndDate: c.EndDate,
            Days: c.Days,
            Reason: c.Reason,
            CertFileId: c.CertFileId,
            ArchiveNote: c.ArchiveNote,
            Status: c.Status.ToString(),
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? displayNames.GetValueOrDefault(a) : null,
            CurrentAssigneeRoleCode: c.CurrentAssigneeRoleCode,
            ManagerDecision: c.ManagerUserId is null ? null : new LEAVE_V1_DecisionDto(
                UserId: c.ManagerUserId,
                DisplayName: displayNames.GetValueOrDefault(c.ManagerUserId.Value),
                Approved: c.ManagerApproved,
                Comment: c.ManagerComment,
                DecidedAt: c.ManagerDecisionAt),
            VpDecision: c.VpUserId is null ? null : new LEAVE_V1_DecisionDto(
                UserId: c.VpUserId,
                DisplayName: displayNames.GetValueOrDefault(c.VpUserId.Value),
                Approved: c.VpApproved,
                Comment: c.VpComment,
                DecidedAt: c.VpDecisionAt),
            HrArchive: c.HrUserId is null ? null : new LEAVE_V1_DecisionDto(
                UserId: c.HrUserId,
                DisplayName: displayNames.GetValueOrDefault(c.HrUserId.Value),
                Approved: c.HrArchivedAt is not null,
                Comment: c.ArchiveNote,
                DecidedAt: c.HrArchivedAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);
}

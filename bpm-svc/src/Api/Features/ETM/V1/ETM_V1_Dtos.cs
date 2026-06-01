using Bpm.Application.Features.ETM.V1;
using Bpm.Domain.Features.ETM.V1;

namespace Bpm.Api.Features.ETM.V1;

public sealed record ETM_V1_ReturnItemDto(string? AssetType, string? Action, string? Status);

public sealed record ETM_V1_SubmitRequest(
    string EmployeeName,
    string EmployeeId,
    DateOnly LastWorkingDate,
    string Reason,
    string? ProvideCertificate);

public sealed record ETM_V1_HandoverRequest(string? OutstandingPayment, IReadOnlyList<ETM_V1_ReturnItemDto> ReturnItems);

public sealed record ETM_V1_DecisionDto(Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record ETM_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string EmployeeName,
    string EmployeeId,
    DateOnly LastWorkingDate,
    string Reason,
    string? ProvideCertificate,
    string? OutstandingPayment,
    IReadOnlyList<ETM_V1_ReturnItemDto> ReturnItems,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    ETM_V1_DecisionDto? ManagerDecision,
    Guid? HandoverByUserId,
    string? HandoverByDisplayName,
    DateTime? HandoverAt,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record ETM_V1_CaseRowResponse(
    Guid Id, string Status, string EmployeeName,
    Guid SubmitterUserId, string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId, string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt, DateTime LastActivityAt);

internal static class ETM_V1_DtoMapping
{
    public static ETM_V1_ReturnItem ToDomain(ETM_V1_ReturnItemDto d) => new(d.AssetType, d.Action, d.Status);
    public static ETM_V1_ReturnItemDto ToDto(ETM_V1_ReturnItem d) => new(d.AssetType, d.Action, d.Status);

    public static ETM_V1_CaseResponse ToResponse(ETM_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            EmployeeName: c.EmployeeName, EmployeeId: c.EmployeeId, LastWorkingDate: c.LastWorkingDate,
            Reason: c.Reason, ProvideCertificate: c.ProvideCertificate,
            OutstandingPayment: c.OutstandingPayment,
            ReturnItems: c.ReturnItems.Select(ToDto).ToList(),
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new ETM_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value), c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            HandoverByUserId: c.HandoverByUserId,
            HandoverByDisplayName: c.HandoverByUserId is { } h ? names.GetValueOrDefault(h) : null,
            HandoverAt: c.HandoverAt,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt, CompletedAt: c.CompletedAt);

    public static ETM_V1_TerminationService.SubmitInput ToServiceInput(Guid submitterUserId, ETM_V1_SubmitRequest req)
        => new(submitterUserId, req.EmployeeName, req.EmployeeId, req.LastWorkingDate, req.Reason, req.ProvideCertificate);
}

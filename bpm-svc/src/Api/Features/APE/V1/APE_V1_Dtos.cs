using Bpm.Application.Features.APE.V1;
using Bpm.Domain.Features.APE.V1;

namespace Bpm.Api.Features.APE.V1;

public sealed record APE_V1_SubmitRequest(
    DateOnly ExpectReceiveDate,
    DateOnly DeductReturnDate,
    string ChargeDepartment,
    string? RechargeOutside,
    string Description,
    decimal Amount,
    string Currency);

public sealed record APE_V1_DecisionDto(
    Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record APE_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    DateOnly ExpectReceiveDate,
    DateOnly DeductReturnDate,
    string ChargeDepartment,
    string? RechargeOutside,
    string Description,
    decimal Amount,
    string Currency,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    APE_V1_DecisionDto? ManagerDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record APE_V1_CaseRowResponse(
    Guid Id,
    string Status,
    decimal Amount,
    string Currency,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class APE_V1_DtoMapping
{
    public static APE_V1_CaseResponse ToResponse(APE_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            ExpectReceiveDate: c.ExpectReceiveDate,
            DeductReturnDate: c.DeductReturnDate,
            ChargeDepartment: c.ChargeDepartment,
            RechargeOutside: c.RechargeOutside,
            Description: c.Description,
            Amount: c.Amount,
            Currency: c.Currency,
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new APE_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value),
                c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);

    public static APE_V1_AdvancePaymentService.SubmitInput ToServiceInput(Guid submitterUserId, APE_V1_SubmitRequest req)
        => new(submitterUserId, req.ExpectReceiveDate, req.DeductReturnDate, req.ChargeDepartment,
               req.RechargeOutside, req.Description, req.Amount, req.Currency);
}

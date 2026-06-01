using Bpm.Application.Features.EOB.V1;
using Bpm.Domain.Features.EOB.V1;

namespace Bpm.Api.Features.EOB.V1;

public sealed record EOB_V1_SetupTaskDto(string? Task, string? Status);

public sealed record EOB_V1_SubmitRequest(
    string FirstName,
    string LastName,
    string BusinessTitle,
    string EmployeeLocation,
    DateOnly OnboardDate,
    string? RequireMailbox,
    string CostCenter,
    string? ContractNumber,
    DateOnly? ContractEffectiveDate,
    DateOnly? ContractExpirationDate);

public sealed record EOB_V1_SetupRequest(IReadOnlyList<EOB_V1_SetupTaskDto> SetupTasks);

public sealed record EOB_V1_DecisionDto(Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record EOB_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string FirstName,
    string LastName,
    string BusinessTitle,
    string EmployeeLocation,
    DateOnly OnboardDate,
    string? RequireMailbox,
    string CostCenter,
    string? ContractNumber,
    DateOnly? ContractEffectiveDate,
    DateOnly? ContractExpirationDate,
    IReadOnlyList<EOB_V1_SetupTaskDto> SetupTasks,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    EOB_V1_DecisionDto? ManagerDecision,
    Guid? SetupByUserId,
    string? SetupByDisplayName,
    DateTime? SetupAt,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record EOB_V1_CaseRowResponse(
    Guid Id, string Status, string NewHireName,
    Guid SubmitterUserId, string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId, string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt, DateTime LastActivityAt);

internal static class EOB_V1_DtoMapping
{
    public static EOB_V1_SetupTask ToDomain(EOB_V1_SetupTaskDto d) => new(d.Task, d.Status);
    public static EOB_V1_SetupTaskDto ToDto(EOB_V1_SetupTask d) => new(d.Task, d.Status);

    public static EOB_V1_CaseResponse ToResponse(EOB_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            FirstName: c.FirstName, LastName: c.LastName, BusinessTitle: c.BusinessTitle,
            EmployeeLocation: c.EmployeeLocation, OnboardDate: c.OnboardDate, RequireMailbox: c.RequireMailbox,
            CostCenter: c.CostCenter, ContractNumber: c.ContractNumber,
            ContractEffectiveDate: c.ContractEffectiveDate, ContractExpirationDate: c.ContractExpirationDate,
            SetupTasks: c.SetupTasks.Select(ToDto).ToList(),
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new EOB_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value), c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            SetupByUserId: c.SetupByUserId,
            SetupByDisplayName: c.SetupByUserId is { } s ? names.GetValueOrDefault(s) : null,
            SetupAt: c.SetupAt,
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt, CompletedAt: c.CompletedAt);

    public static EOB_V1_OnboardingService.SubmitInput ToServiceInput(Guid submitterUserId, EOB_V1_SubmitRequest req)
        => new(submitterUserId, req.FirstName, req.LastName, req.BusinessTitle, req.EmployeeLocation, req.OnboardDate,
               req.RequireMailbox, req.CostCenter, req.ContractNumber, req.ContractEffectiveDate, req.ContractExpirationDate);
}

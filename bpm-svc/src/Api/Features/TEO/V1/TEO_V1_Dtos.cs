using Bpm.Application.Features.TEO.V1;
using Bpm.Domain.Features.TEO.V1;

namespace Bpm.Api.Features.TEO.V1;

public sealed record TEO_V1_ExpenseItemDto(
    DateOnly Date, string? Country, string? Category, string? Description, string? Amount, string? AmountLcy);

public sealed record TEO_V1_SubmitRequest(
    string TravelRequestNo,
    IReadOnlyList<TEO_V1_ExpenseItemDto> ExpenseItems);

public sealed record TEO_V1_DecisionDto(
    Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record TEO_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string TravelRequestNo,
    IReadOnlyList<TEO_V1_ExpenseItemDto> ExpenseItems,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    string? CurrentAssigneeRoleCode,
    TEO_V1_DecisionDto? ManagerDecision,
    TEO_V1_DecisionDto? FinanceDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record TEO_V1_CaseRowResponse(
    Guid Id, string Status, int ItemCount,
    Guid SubmitterUserId, string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId, string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt, DateTime LastActivityAt);

internal static class TEO_V1_DtoMapping
{
    public static TEO_V1_ExpenseItem ToDomain(TEO_V1_ExpenseItemDto d)
        => new(d.Date, d.Country, d.Category, d.Description, d.Amount, d.AmountLcy);
    public static TEO_V1_ExpenseItemDto ToDto(TEO_V1_ExpenseItem d)
        => new(d.Date, d.Country, d.Category, d.Description, d.Amount, d.AmountLcy);

    public static TEO_V1_CaseResponse ToResponse(TEO_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            TravelRequestNo: c.TravelRequestNo,
            ExpenseItems: c.ExpenseItems.Select(ToDto).ToList(),
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            CurrentAssigneeRoleCode: c.CurrentAssigneeRoleCode,
            ManagerDecision: c.ManagerUserId is null ? null : new TEO_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value), c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            FinanceDecision: c.FinanceUserId is null ? null : new TEO_V1_DecisionDto(
                c.FinanceUserId, names.GetValueOrDefault(c.FinanceUserId.Value), c.FinanceApproved, c.FinanceComment, c.FinanceDecisionAt),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt, CompletedAt: c.CompletedAt);

    public static TEO_V1_TravelExpenseService.SubmitInput ToServiceInput(Guid submitterUserId, TEO_V1_SubmitRequest req)
        => new(submitterUserId, req.TravelRequestNo, req.ExpenseItems.Select(ToDomain).ToList());
}

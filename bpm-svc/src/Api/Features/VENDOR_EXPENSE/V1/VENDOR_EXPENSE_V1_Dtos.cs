using Bpm.Application.Features.VENDOR_EXPENSE.V1;
using Bpm.Domain.Features.VENDOR_EXPENSE.V1;

namespace Bpm.Api.Features.VENDOR_EXPENSE.V1;

/// <summary>Invoice line item on the wire (task_fill rep_iyru row).</summary>
public sealed record VENDOR_EXPENSE_V1_InvoiceDto(
    DateOnly? InvoiceDate,
    string?   InvoiceNo,
    string?   ChargeTo,
    string?   Project,
    string?   Category,
    string?   Amount,
    string?   Currency,
    string?   Description);

public sealed record VENDOR_EXPENSE_V1_SubmitRequest(
    string? Vendor,
    string? SubmitterComment,
    IReadOnlyList<VENDOR_EXPENSE_V1_InvoiceDto> Invoices);

/// <summary>
/// Decision payload for every approval stage. <c>Comment</c> is required
/// only for the supervisor reject (spec sets promptComment: required
/// there); the controller leaves enforcement to the client + service.
/// </summary>
public sealed record VENDOR_EXPENSE_V1_DecisionRequest(bool Approve, string? Comment);

public sealed record VENDOR_EXPENSE_V1_DecisionDto(
    Guid? UserId,
    string? DisplayName,
    bool? Approved,
    string? Comment,
    DateTime? DecidedAt);

public sealed record VENDOR_EXPENSE_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string? Vendor,
    string? SubmitterComment,
    IReadOnlyList<VENDOR_EXPENSE_V1_InvoiceDto> Invoices,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    string? CurrentAssigneeRoleCode,
    VENDOR_EXPENSE_V1_DecisionDto? SupervisorDecision,
    VENDOR_EXPENSE_V1_DecisionDto? ProcurementDecision,
    VENDOR_EXPENSE_V1_DecisionDto? SignDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record VENDOR_EXPENSE_V1_CaseRowResponse(
    Guid Id,
    string Status,
    string? Vendor,
    int InvoiceCount,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class VENDOR_EXPENSE_V1_DtoMapping
{
    public static VENDOR_EXPENSE_V1_Invoice ToDomain(VENDOR_EXPENSE_V1_InvoiceDto dto)
        => new(
            InvoiceDate: dto.InvoiceDate,
            InvoiceNo:   dto.InvoiceNo,
            ChargeTo:    dto.ChargeTo,
            Project:     dto.Project,
            Category:    dto.Category,
            Amount:      dto.Amount,
            Currency:    dto.Currency,
            Description: dto.Description);

    public static VENDOR_EXPENSE_V1_InvoiceDto ToDto(VENDOR_EXPENSE_V1_Invoice domain)
        => new(
            InvoiceDate: domain.InvoiceDate,
            InvoiceNo:   domain.InvoiceNo,
            ChargeTo:    domain.ChargeTo,
            Project:     domain.Project,
            Category:    domain.Category,
            Amount:      domain.Amount,
            Currency:    domain.Currency,
            Description: domain.Description);

    public static VENDOR_EXPENSE_V1_CaseResponse ToResponse(
        VENDOR_EXPENSE_V1_Case c,
        IReadOnlyDictionary<Guid, string> displayNames)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: displayNames.GetValueOrDefault(c.SubmitterUserId),
            Vendor: c.Vendor,
            SubmitterComment: c.SubmitterComment,
            Invoices: c.Invoices.Select(ToDto).ToList(),
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a
                ? displayNames.GetValueOrDefault(a) : null,
            CurrentAssigneeRoleCode: c.CurrentAssigneeRoleCode,
            SupervisorDecision: Decision(c.SupervisorUserId, c.SupervisorApproved, c.SupervisorComment, c.SupervisorDecisionAt, displayNames),
            ProcurementDecision: Decision(c.ProcurementUserId, c.ProcurementApproved, c.ProcurementComment, c.ProcurementDecisionAt, displayNames),
            SignDecision: Decision(c.SignUserId, c.SignApproved, c.SignComment, c.SignDecisionAt, displayNames),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);

    private static VENDOR_EXPENSE_V1_DecisionDto? Decision(
        Guid? userId, bool? approved, string? comment, DateTime? decidedAt,
        IReadOnlyDictionary<Guid, string> displayNames)
        => userId is null ? null : new VENDOR_EXPENSE_V1_DecisionDto(
            UserId: userId,
            DisplayName: displayNames.GetValueOrDefault(userId.Value),
            Approved: approved,
            Comment: comment,
            DecidedAt: decidedAt);

    public static VENDOR_EXPENSE_V1_VendorExpenseService.SubmitInput ToServiceInput(
        Guid submitterUserId,
        VENDOR_EXPENSE_V1_SubmitRequest req)
        => new(submitterUserId, req.Vendor, req.SubmitterComment, req.Invoices.Select(ToDomain).ToList());
}

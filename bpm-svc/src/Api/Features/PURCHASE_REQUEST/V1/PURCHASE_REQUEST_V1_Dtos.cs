using Bpm.Application.Features.PURCHASE_REQUEST.V1;
using Bpm.Domain.Features.PURCHASE_REQUEST.V1;

namespace Bpm.Api.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// Invoice line item on the wire. Spec field ids <c>charge to</c>
/// (with a space) and <c>currecny</c> (typo) are remapped to
/// <c>ChargeTo</c> / <c>Currency</c> here; the bundle's spec.json
/// stays untouched.
/// </summary>
public sealed record PURCHASE_REQUEST_V1_InvoiceDto(
    DateOnly InvoiceDate,
    string?  InvoiceNo,
    string?  ChargeTo,
    string?  Project,
    string?  Category,
    string?  Amount,
    string?  Currency,
    Guid?    AttachmentFileId,
    string?  Description);

public sealed record PURCHASE_REQUEST_V1_SubmitRequest(
    string SubmitterComment,
    IReadOnlyList<PURCHASE_REQUEST_V1_InvoiceDto> Invoices);

public sealed record PURCHASE_REQUEST_V1_DecisionRequest(string? Comment);

public sealed record PURCHASE_REQUEST_V1_DecisionDto(
    Guid? UserId,
    string? DisplayName,
    bool? Approved,
    string? Comment,
    DateTime? DecidedAt);

public sealed record PURCHASE_REQUEST_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string SubmitterComment,
    IReadOnlyList<PURCHASE_REQUEST_V1_InvoiceDto> Invoices,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    string? CurrentAssigneeRoleCode,
    PURCHASE_REQUEST_V1_DecisionDto? DeptHeadDecision,
    PURCHASE_REQUEST_V1_DecisionDto? FinanceDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record PURCHASE_REQUEST_V1_CaseRowResponse(
    Guid Id,
    string Status,
    int InvoiceCount,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class PURCHASE_REQUEST_V1_DtoMapping
{
    public static PURCHASE_REQUEST_V1_Invoice ToDomain(PURCHASE_REQUEST_V1_InvoiceDto dto)
        => new(
            InvoiceDate:      dto.InvoiceDate,
            InvoiceNo:        dto.InvoiceNo,
            ChargeTo:         dto.ChargeTo,
            Project:          dto.Project,
            Category:         dto.Category,
            Amount:           dto.Amount,
            Currency:         dto.Currency,
            AttachmentFileId: dto.AttachmentFileId,
            Description:      dto.Description);

    public static PURCHASE_REQUEST_V1_InvoiceDto ToDto(PURCHASE_REQUEST_V1_Invoice domain)
        => new(
            InvoiceDate:      domain.InvoiceDate,
            InvoiceNo:        domain.InvoiceNo,
            ChargeTo:         domain.ChargeTo,
            Project:          domain.Project,
            Category:         domain.Category,
            Amount:           domain.Amount,
            Currency:         domain.Currency,
            AttachmentFileId: domain.AttachmentFileId,
            Description:      domain.Description);

    public static PURCHASE_REQUEST_V1_CaseResponse ToResponse(
        PURCHASE_REQUEST_V1_Case c,
        IReadOnlyDictionary<Guid, string> displayNames)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: displayNames.GetValueOrDefault(c.SubmitterUserId),
            SubmitterComment: c.SubmitterComment,
            Invoices: c.Invoices.Select(ToDto).ToList(),
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a
                ? displayNames.GetValueOrDefault(a) : null,
            CurrentAssigneeRoleCode: c.CurrentAssigneeRoleCode,
            DeptHeadDecision: c.DeptHeadUserId is null ? null : new PURCHASE_REQUEST_V1_DecisionDto(
                UserId: c.DeptHeadUserId,
                DisplayName: displayNames.GetValueOrDefault(c.DeptHeadUserId.Value),
                Approved: c.DeptHeadApproved,
                Comment: c.DeptHeadComment,
                DecidedAt: c.DeptHeadDecisionAt),
            FinanceDecision: c.FinanceUserId is null ? null : new PURCHASE_REQUEST_V1_DecisionDto(
                UserId: c.FinanceUserId,
                DisplayName: displayNames.GetValueOrDefault(c.FinanceUserId.Value),
                Approved: c.FinanceApproved,
                Comment: c.FinanceComment,
                DecidedAt: c.FinanceDecisionAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);

    public static PURCHASE_REQUEST_V1_PurchaseRequestService.SubmitInput ToServiceInput(
        Guid submitterUserId,
        PURCHASE_REQUEST_V1_SubmitRequest req)
        => new(submitterUserId, req.SubmitterComment, req.Invoices.Select(ToDomain).ToList());
}

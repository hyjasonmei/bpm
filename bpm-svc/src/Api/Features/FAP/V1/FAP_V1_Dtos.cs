using Bpm.Application.Features.FAP.V1;
using Bpm.Domain.Features.FAP.V1;

namespace Bpm.Api.Features.FAP.V1;

public sealed record FAP_V1_PurchaseItemDto(string? Category, string? ItemSpec, int Qty);

public sealed record FAP_V1_SubmitRequest(
    IReadOnlyList<FAP_V1_PurchaseItemDto> PurchaseItems,
    string ShippingLocation,
    string ChargeTo,
    string Purpose,
    DateOnly? ExpectedDate,
    string? Note);

public sealed record FAP_V1_DecisionDto(Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);

public sealed record FAP_V1_VerificationDto(Guid? UserId, string? DisplayName, string? Received, string? Remark, DateTime? VerifiedAt);

public sealed record FAP_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    IReadOnlyList<FAP_V1_PurchaseItemDto> PurchaseItems,
    string ShippingLocation,
    string ChargeTo,
    string Purpose,
    DateOnly? ExpectedDate,
    string? Note,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    FAP_V1_DecisionDto? ManagerDecision,
    string? PurchaseOrderNo,
    FAP_V1_VerificationDto? Verification,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record FAP_V1_CaseRowResponse(
    Guid Id, string Status, int ItemCount,
    Guid SubmitterUserId, string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId, string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt, DateTime LastActivityAt);

internal static class FAP_V1_DtoMapping
{
    public static FAP_V1_PurchaseItem ToDomain(FAP_V1_PurchaseItemDto d) => new(d.Category, d.ItemSpec, d.Qty);
    public static FAP_V1_PurchaseItemDto ToDto(FAP_V1_PurchaseItem d) => new(d.Category, d.ItemSpec, d.Qty);

    public static FAP_V1_CaseResponse ToResponse(FAP_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            PurchaseItems: c.PurchaseItems.Select(ToDto).ToList(),
            ShippingLocation: c.ShippingLocation,
            ChargeTo: c.ChargeTo,
            Purpose: c.Purpose,
            ExpectedDate: c.ExpectedDate,
            Note: c.Note,
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new FAP_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value), c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            PurchaseOrderNo: c.PurchaseOrderNo,
            Verification: c.VerifiedByUserId is null ? null : new FAP_V1_VerificationDto(
                c.VerifiedByUserId, names.GetValueOrDefault(c.VerifiedByUserId.Value), c.Received, c.VerificationRemark, c.VerifiedAt),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt, CompletedAt: c.CompletedAt);

    public static FAP_V1_PurchaseService.SubmitInput ToServiceInput(Guid submitterUserId, FAP_V1_SubmitRequest req)
        => new(submitterUserId, req.PurchaseItems.Select(ToDomain).ToList(), req.ShippingLocation, req.ChargeTo, req.Purpose, req.ExpectedDate, req.Note);
}

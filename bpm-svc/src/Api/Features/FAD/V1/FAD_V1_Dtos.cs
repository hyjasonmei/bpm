using Bpm.Application.Features.FAD.V1;
using Bpm.Domain.Features.FAD.V1;

namespace Bpm.Api.Features.FAD.V1;

public sealed record FAD_V1_SubmitRequest(
    string DisposalReason,
    string AssetId,
    string AssetName,
    string? Description,
    Guid? PhotoFileId);

public sealed record FAD_V1_DecisionDto(Guid? UserId, string? DisplayName, bool? Approved, string? Comment, DateTime? DecidedAt);
public sealed record FAD_V1_ConfirmDto(Guid? UserId, string? DisplayName, string? HandlingResult, string? Remark, DateTime? ConfirmedAt);

public sealed record FAD_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string DisposalReason,
    string AssetId,
    string AssetName,
    string? Description,
    Guid? PhotoFileId,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    FAD_V1_DecisionDto? ManagerDecision,
    FAD_V1_ConfirmDto? Confirmation,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record FAD_V1_CaseRowResponse(
    Guid Id, string Status, string AssetName,
    Guid SubmitterUserId, string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId, string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt, DateTime LastActivityAt);

internal static class FAD_V1_DtoMapping
{
    public static FAD_V1_CaseResponse ToResponse(FAD_V1_Case c, IReadOnlyDictionary<Guid, string> names)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: names.GetValueOrDefault(c.SubmitterUserId),
            DisposalReason: c.DisposalReason,
            AssetId: c.AssetId,
            AssetName: c.AssetName,
            Description: c.Description,
            PhotoFileId: c.PhotoFileId,
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a ? names.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new FAD_V1_DecisionDto(
                c.ManagerUserId, names.GetValueOrDefault(c.ManagerUserId.Value), c.ManagerApproved, c.ManagerComment, c.ManagerDecisionAt),
            Confirmation: c.ConfirmedByUserId is null ? null : new FAD_V1_ConfirmDto(
                c.ConfirmedByUserId, names.GetValueOrDefault(c.ConfirmedByUserId.Value), c.HandlingResult, c.ConfirmRemark, c.ConfirmedAt),
            SubmittedAt: c.SubmittedAt, LastActivityAt: c.LastActivityAt, CompletedAt: c.CompletedAt);

    public static FAD_V1_DisposalService.SubmitInput ToServiceInput(Guid submitterUserId, FAD_V1_SubmitRequest req)
        => new(submitterUserId, req.DisposalReason, req.AssetId, req.AssetName, req.Description, req.PhotoFileId);
}

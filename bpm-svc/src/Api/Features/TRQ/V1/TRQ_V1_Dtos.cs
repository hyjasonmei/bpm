using Bpm.Application.Features.TRQ.V1;
using Bpm.Domain.Features.TRQ.V1;

namespace Bpm.Api.Features.TRQ.V1;

/// <summary>
/// Submit / resubmit payload — mirrors the bundle's userTask `req`
/// fields one-to-one.
/// </summary>
public sealed record TRQ_V1_SubmitRequest(
    string TravelType,
    string DepartureCity,
    string DestinationCity,
    DateOnly DepartDate,
    DateOnly? ReturnDate,
    string ChargeTo,
    string TravelPurpose,
    string? PassportName,
    string? SeatPreference,
    string? PickupRequired);

public sealed record TRQ_V1_DecisionDto(
    Guid? UserId,
    string? DisplayName,
    bool? Approved,
    string? Comment,
    DateTime? DecidedAt);

public sealed record TRQ_V1_CaseResponse(
    Guid Id,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    string TravelType,
    string DepartureCity,
    string DestinationCity,
    DateOnly DepartDate,
    DateOnly? ReturnDate,
    string ChargeTo,
    string TravelPurpose,
    string? PassportName,
    string? SeatPreference,
    string? PickupRequired,
    string Status,
    int RoundCount,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    TRQ_V1_DecisionDto? ManagerDecision,
    DateTime SubmittedAt,
    DateTime LastActivityAt,
    DateTime? CompletedAt);

public sealed record TRQ_V1_CaseRowResponse(
    Guid Id,
    string Status,
    string DepartureCity,
    string DestinationCity,
    Guid SubmitterUserId,
    string? SubmitterDisplayName,
    Guid? CurrentAssigneeUserId,
    string? CurrentAssigneeDisplayName,
    DateTime SubmittedAt,
    DateTime LastActivityAt);

internal static class TRQ_V1_DtoMapping
{
    public static TRQ_V1_CaseResponse ToResponse(
        TRQ_V1_Case c,
        IReadOnlyDictionary<Guid, string> displayNames)
        => new(
            Id: c.Id,
            SubmitterUserId: c.SubmitterUserId,
            SubmitterDisplayName: displayNames.GetValueOrDefault(c.SubmitterUserId),
            TravelType: c.TravelType,
            DepartureCity: c.DepartureCity,
            DestinationCity: c.DestinationCity,
            DepartDate: c.DepartDate,
            ReturnDate: c.ReturnDate,
            ChargeTo: c.ChargeTo,
            TravelPurpose: c.TravelPurpose,
            PassportName: c.PassportName,
            SeatPreference: c.SeatPreference,
            PickupRequired: c.PickupRequired,
            Status: c.Status.ToString(),
            RoundCount: c.RoundCount,
            CurrentAssigneeUserId: c.CurrentAssigneeUserId,
            CurrentAssigneeDisplayName: c.CurrentAssigneeUserId is { } a
                ? displayNames.GetValueOrDefault(a) : null,
            ManagerDecision: c.ManagerUserId is null ? null : new TRQ_V1_DecisionDto(
                UserId: c.ManagerUserId,
                DisplayName: displayNames.GetValueOrDefault(c.ManagerUserId.Value),
                Approved: c.ManagerApproved,
                Comment: c.ManagerComment,
                DecidedAt: c.ManagerDecisionAt),
            SubmittedAt: c.SubmittedAt,
            LastActivityAt: c.LastActivityAt,
            CompletedAt: c.CompletedAt);

    public static TRQ_V1_TravelRequestService.SubmitInput ToServiceInput(
        Guid submitterUserId,
        TRQ_V1_SubmitRequest req)
        => new(
            SubmitterUserId: submitterUserId,
            TravelType: req.TravelType,
            DepartureCity: req.DepartureCity,
            DestinationCity: req.DestinationCity,
            DepartDate: req.DepartDate,
            ReturnDate: req.ReturnDate,
            ChargeTo: req.ChargeTo,
            TravelPurpose: req.TravelPurpose,
            PassportName: req.PassportName,
            SeatPreference: req.SeatPreference,
            PickupRequired: req.PickupRequired);
}

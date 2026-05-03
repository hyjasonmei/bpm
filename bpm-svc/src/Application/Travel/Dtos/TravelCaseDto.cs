using Bpm.Domain.Cases;
using Bpm.Domain.States;

namespace Bpm.Application.Travel.Dtos;

public sealed record TravelCaseDto(
    Guid Id,
    string TenantCode,
    string FlowCode,
    TravelState State,
    string ApplicantUserId,
    string DestinationType,
    string Destination,
    DateOnly DepartDate,
    DateOnly ReturnDate,
    string Purpose,
    decimal EstimatedCost,
    string? TicketRef,
    string? HotelRef,
    string? BookNote,
    string? CurrentApproverUserId,
    string? ManagerApproverUserId,
    DateTime? ManagerApprovedAt,
    string? VpApproverUserId,
    DateTime? VpApprovedAt,
    string? AdminBookerUserId,
    DateTime? AdminBookedAt,
    string? RejectedByUserId,
    DateTime? RejectedAt,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy
)
{
    public static TravelCaseDto FromDomain(TravelCase c) => new(
        c.Id, c.TenantCode, c.FlowCode, c.State, c.ApplicantUserId,
        c.DestinationType, c.Destination, c.DepartDate, c.ReturnDate,
        c.Purpose, c.EstimatedCost,
        c.TicketRef, c.HotelRef, c.BookNote,
        c.CurrentApproverUserId,
        c.ManagerApproverUserId, c.ManagerApprovedAt,
        c.VpApproverUserId, c.VpApprovedAt,
        c.AdminBookerUserId, c.AdminBookedAt,
        c.RejectedByUserId, c.RejectedAt, c.RejectionReason,
        c.CreatedAt, c.UpdatedAt, c.CreatedBy
    );
}

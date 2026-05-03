using Bpm.Domain.Common;
using Bpm.Domain.Events;
using Bpm.Domain.States;

namespace Bpm.Domain.Cases;

public sealed class TravelCase : AuditableEntity
{
    private readonly List<TravelDomainEvent> _events = new();

    public Guid Id { get; private set; }
    public string TenantCode { get; private set; } = "";
    public string FlowCode { get; private set; } = "TRAVEL";

    public TravelState State { get; private set; }
    public string ApplicantUserId { get; private set; } = "";

    // task_request fields
    public string DestinationType { get; private set; } = "";  // domestic | international
    public string Destination { get; private set; } = "";
    public DateOnly DepartDate { get; private set; }
    public DateOnly ReturnDate { get; private set; }
    public string Purpose { get; private set; } = "";
    public decimal EstimatedCost { get; private set; }

    // task_admin_book fields
    public string? TicketRef { get; private set; }
    public string? HotelRef { get; private set; }
    public string? BookNote { get; private set; }

    // Approval audit
    public string? CurrentApproverUserId { get; private set; }
    public string? ManagerApproverUserId { get; private set; }
    public DateTime? ManagerApprovedAt { get; private set; }
    public string? VpApproverUserId { get; private set; }
    public DateTime? VpApprovedAt { get; private set; }
    public string? AdminBookerUserId { get; private set; }
    public DateTime? AdminBookedAt { get; private set; }

    public string? RejectedByUserId { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public IReadOnlyList<TravelDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

    private TravelCase() { }

    public static TravelCase Submit(
        string tenantCode,
        string applicantUserId,
        string destinationType,
        string destination,
        DateOnly departDate,
        DateOnly returnDate,
        string purpose,
        decimal estimatedCost,
        string firstApproverUserId,
        DateTime now)
    {
        var c = new TravelCase
        {
            Id = Guid.NewGuid(),
            TenantCode = tenantCode,
            FlowCode = "TRAVEL",
            State = TravelState.PendingManagerApproval,
            ApplicantUserId = applicantUserId,
            DestinationType = destinationType,
            Destination = destination,
            DepartDate = departDate,
            ReturnDate = returnDate,
            Purpose = purpose,
            EstimatedCost = estimatedCost,
            CurrentApproverUserId = firstApproverUserId,
        };
        c._events.Add(new TravelSubmitted(c.Id, now, applicantUserId, destinationType, destination, estimatedCost));
        return c;
    }

    public void ManagerApprove(string approverUserId, string? nextApproverIfIntl, DateTime now)
    {
        if (State != TravelState.PendingManagerApproval)
            throw new InvalidOperationException($"Cannot manager-approve in state {State}.");

        ManagerApproverUserId = approverUserId;
        ManagerApprovedAt = now;
        var from = State;

        if (DestinationType == "international")
        {
            if (string.IsNullOrEmpty(nextApproverIfIntl))
                throw new InvalidOperationException("VP approver required for international travel.");
            State = TravelState.PendingVpApproval;
            CurrentApproverUserId = nextApproverIfIntl;
        }
        else
        {
            State = TravelState.PendingAdminBook;
            CurrentApproverUserId = null;
        }
        _events.Add(new TravelStepApproved(Id, now, from, State, approverUserId));
    }

    public void VpApprove(string approverUserId, DateTime now)
    {
        if (State != TravelState.PendingVpApproval)
            throw new InvalidOperationException($"Cannot VP-approve in state {State}.");

        VpApproverUserId = approverUserId;
        VpApprovedAt = now;
        var from = State;
        State = TravelState.PendingAdminBook;
        CurrentApproverUserId = null;
        _events.Add(new TravelStepApproved(Id, now, from, State, approverUserId));
    }

    public void Reject(string approverUserId, string reason, DateTime now)
    {
        if (State != TravelState.PendingManagerApproval && State != TravelState.PendingVpApproval)
            throw new InvalidOperationException($"Cannot reject in state {State}.");

        var from = State;
        RejectedByUserId = approverUserId;
        RejectedAt = now;
        RejectionReason = reason;
        State = TravelState.Rejected;
        CurrentApproverUserId = null;
        _events.Add(new TravelRejected(Id, now, from, approverUserId, reason));
    }

    public void Book(string adminUserId, string ticketRef, string? hotelRef, string? bookNote, DateTime now)
    {
        if (State != TravelState.PendingAdminBook)
            throw new InvalidOperationException($"Cannot book in state {State}.");

        AdminBookerUserId = adminUserId;
        AdminBookedAt = now;
        TicketRef = ticketRef;
        HotelRef = hotelRef;
        BookNote = bookNote;
        State = TravelState.Completed;
        _events.Add(new TravelBooked(Id, now, adminUserId, ticketRef));
        _events.Add(new TravelCompleted(Id, now));
    }
}

using Bpm.Domain.Common;
using Bpm.Domain.Events;
using Bpm.Domain.States;

namespace Bpm.Domain.Cases;

public sealed class PurchaseCase : AuditableEntity
{
    public const decimal FinanceThreshold = 10000m;   // spec.decisions[gateway_after_manager]
    public const decimal CeoThreshold = 100000m;      // spec.decisions[gateway_after_finance]

    private readonly List<PurchaseDomainEvent> _events = new();

    public Guid Id { get; private set; }
    public string TenantCode { get; private set; } = "";
    public string FlowCode { get; private set; } = "PURCHASE";

    public PurchaseState State { get; private set; }
    public string ApplicantUserId { get; private set; } = "";

    // task_request fields (spec.userTasks[0].fields)
    public string Vendor { get; private set; } = "";
    public string Category { get; private set; } = "";          // office | it | service | other
    public decimal Amount { get; private set; }
    public string Items { get; private set; } = "";
    public string Justification { get; private set; } = "";
    public string? QuoteFileName { get; private set; }          // required when Amount >= 10000

    // task_purchase_exec fields (spec.userTasks[1].fields)
    public string? PoNumber { get; private set; }
    public DateOnly? ExpectedDelivery { get; private set; }
    public string? ExecNote { get; private set; }

    // Approval audit
    public string? CurrentApproverUserId { get; private set; }
    public string? ManagerApproverUserId { get; private set; }
    public DateTime? ManagerApprovedAt { get; private set; }
    public string? FinanceApproverUserId { get; private set; }
    public DateTime? FinanceApprovedAt { get; private set; }
    public string? CeoApproverUserId { get; private set; }
    public DateTime? CeoApprovedAt { get; private set; }
    public string? PurchaseExecUserId { get; private set; }
    public DateTime? PurchaseExecAt { get; private set; }

    // Rejection
    public string? RejectedByUserId { get; private set; }
    public DateTime? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public IReadOnlyList<PurchaseDomainEvent> DomainEvents => _events;
    public void ClearDomainEvents() => _events.Clear();

    private PurchaseCase() { }

    public static PurchaseCase Submit(
        string tenantCode,
        string applicantUserId,
        string vendor,
        string category,
        decimal amount,
        string items,
        string justification,
        string? quoteFileName,
        string firstApproverUserId,
        DateTime now)
    {
        var c = new PurchaseCase
        {
            Id = Guid.NewGuid(),
            TenantCode = tenantCode,
            FlowCode = "PURCHASE",
            State = PurchaseState.PendingManagerApproval,
            ApplicantUserId = applicantUserId,
            Vendor = vendor,
            Category = category,
            Amount = amount,
            Items = items,
            Justification = justification,
            QuoteFileName = quoteFileName,
            CurrentApproverUserId = firstApproverUserId,
        };
        c._events.Add(new PurchaseSubmitted(c.Id, now, applicantUserId, amount, vendor, category));
        return c;
    }

    public void ManagerApprove(string approverUserId, string? nextApproverUserId, DateTime now)
    {
        if (State != PurchaseState.PendingManagerApproval)
            throw new InvalidOperationException($"Cannot manager-approve in state {State}.");

        ManagerApproverUserId = approverUserId;
        ManagerApprovedAt = now;
        var from = State;

        // gateway_after_manager: amount >= 10000 → approval_finance, else → task_purchase_exec
        if (Amount >= FinanceThreshold)
        {
            if (string.IsNullOrEmpty(nextApproverUserId))
                throw new InvalidOperationException("Finance approver is required when amount >= 10000.");
            State = PurchaseState.PendingFinanceApproval;
            CurrentApproverUserId = nextApproverUserId;
        }
        else
        {
            State = PurchaseState.PendingPurchaseExec;
            CurrentApproverUserId = null;
        }
        _events.Add(new PurchaseStepApproved(Id, now, from, State, approverUserId));
    }

    public void FinanceApprove(string approverUserId, string? nextApproverUserId, DateTime now)
    {
        if (State != PurchaseState.PendingFinanceApproval)
            throw new InvalidOperationException($"Cannot finance-approve in state {State}.");

        FinanceApproverUserId = approverUserId;
        FinanceApprovedAt = now;
        var from = State;

        // gateway_after_finance: amount >= 100000 → approval_ceo, else → task_purchase_exec
        if (Amount >= CeoThreshold)
        {
            if (string.IsNullOrEmpty(nextApproverUserId))
                throw new InvalidOperationException("CEO approver is required when amount >= 100000.");
            State = PurchaseState.PendingCeoApproval;
            CurrentApproverUserId = nextApproverUserId;
        }
        else
        {
            State = PurchaseState.PendingPurchaseExec;
            CurrentApproverUserId = null;
        }
        _events.Add(new PurchaseStepApproved(Id, now, from, State, approverUserId));
    }

    public void CeoApprove(string approverUserId, DateTime now)
    {
        if (State != PurchaseState.PendingCeoApproval)
            throw new InvalidOperationException($"Cannot CEO-approve in state {State}.");

        CeoApproverUserId = approverUserId;
        CeoApprovedAt = now;
        var from = State;
        State = PurchaseState.PendingPurchaseExec;
        CurrentApproverUserId = null;
        _events.Add(new PurchaseStepApproved(Id, now, from, State, approverUserId));
    }

    public void Reject(string approverUserId, string reason, DateTime now)
    {
        if (State != PurchaseState.PendingManagerApproval &&
            State != PurchaseState.PendingFinanceApproval &&
            State != PurchaseState.PendingCeoApproval)
            throw new InvalidOperationException($"Cannot reject in state {State}.");

        var from = State;
        RejectedByUserId = approverUserId;
        RejectedAt = now;
        RejectionReason = reason;
        State = PurchaseState.Rejected;
        CurrentApproverUserId = null;
        _events.Add(new PurchaseRejected(Id, now, from, approverUserId, reason));
    }

    public void Execute(string execUserId, string poNumber, DateOnly expectedDelivery, string? execNote, DateTime now)
    {
        if (State != PurchaseState.PendingPurchaseExec)
            throw new InvalidOperationException($"Cannot execute in state {State}.");

        PurchaseExecUserId = execUserId;
        PurchaseExecAt = now;
        PoNumber = poNumber;
        ExpectedDelivery = expectedDelivery;
        ExecNote = execNote;
        State = PurchaseState.Completed;
        _events.Add(new PurchaseExecuted(Id, now, execUserId, poNumber, expectedDelivery));
        _events.Add(new PurchaseCompleted(Id, now));
    }
}

using Bpm.Domain.Cases;
using Bpm.Domain.States;

namespace Bpm.Application.Purchase.Dtos;

public sealed record PurchaseCaseDto(
    Guid Id,
    string TenantCode,
    string FlowCode,
    PurchaseState State,
    string ApplicantUserId,
    string Vendor,
    string Category,
    decimal Amount,
    string Items,
    string Justification,
    string? QuoteFileName,
    string? PoNumber,
    DateOnly? ExpectedDelivery,
    string? ExecNote,
    string? CurrentApproverUserId,
    string? ManagerApproverUserId,
    DateTime? ManagerApprovedAt,
    string? FinanceApproverUserId,
    DateTime? FinanceApprovedAt,
    string? CeoApproverUserId,
    DateTime? CeoApprovedAt,
    string? PurchaseExecUserId,
    DateTime? PurchaseExecAt,
    string? RejectedByUserId,
    DateTime? RejectedAt,
    string? RejectionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy
)
{
    public static PurchaseCaseDto FromDomain(PurchaseCase c) => new(
        c.Id,
        c.TenantCode,
        c.FlowCode,
        c.State,
        c.ApplicantUserId,
        c.Vendor,
        c.Category,
        c.Amount,
        c.Items,
        c.Justification,
        c.QuoteFileName,
        c.PoNumber,
        c.ExpectedDelivery,
        c.ExecNote,
        c.CurrentApproverUserId,
        c.ManagerApproverUserId,
        c.ManagerApprovedAt,
        c.FinanceApproverUserId,
        c.FinanceApprovedAt,
        c.CeoApproverUserId,
        c.CeoApprovedAt,
        c.PurchaseExecUserId,
        c.PurchaseExecAt,
        c.RejectedByUserId,
        c.RejectedAt,
        c.RejectionReason,
        c.CreatedAt,
        c.UpdatedAt,
        c.CreatedBy
    );
}

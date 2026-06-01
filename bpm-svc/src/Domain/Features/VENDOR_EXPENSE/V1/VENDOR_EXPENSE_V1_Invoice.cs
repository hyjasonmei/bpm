namespace Bpm.Domain.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// One row inside the task_fill <c>rep_iyru</c> repeater (新多筆群組).
/// Persisted as a JSON array on the case row (single TEXT column) per
/// the project's DB conventions (rule 6: keep JSON queries out of the
/// DB layer).
///
/// Every field is optional in the spec (the repeater only sets
/// <c>minCount: 1</c>, no per-field <c>required</c>), so
/// <see cref="InvoiceDate"/> is a nullable <see cref="DateOnly"/> here —
/// unlike the PURCHASE_REQUEST reference which made the date mandatory.
/// </summary>
public sealed record VENDOR_EXPENSE_V1_Invoice(
    DateOnly? InvoiceDate,
    string?   InvoiceNo,
    string?   ChargeTo,
    string?   Project,
    string?   Category,
    string?   Amount,
    string?   Currency,
    string?   Description);

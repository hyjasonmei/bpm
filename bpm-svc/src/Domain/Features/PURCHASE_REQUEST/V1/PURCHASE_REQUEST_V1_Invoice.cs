namespace Bpm.Domain.Features.PURCHASE_REQUEST.V1;

/// <summary>
/// One row inside the task_apply Invoices repeater. Persisted as a JSON
/// array on the case row (single TEXT column) per the project's DB
/// conventions (rule 6: keep JSON queries out of the DB layer).
///
/// Spec field ids `charge to` (with a space) and `currecny` (typo) are
/// remapped on the wire to <c>ChargeTo</c> / <c>Currency</c> for sanity
/// — the bundle's spec.json stays untouched.
/// </summary>
public sealed record PURCHASE_REQUEST_V1_Invoice(
    DateOnly InvoiceDate,
    string?  InvoiceNo,
    string?  ChargeTo,
    string?  Project,
    string?  Category,
    string?  Amount,
    string?  Currency,
    Guid?    AttachmentFileId,
    string?  Description);

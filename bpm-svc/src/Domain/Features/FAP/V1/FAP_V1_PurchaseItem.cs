namespace Bpm.Domain.Features.FAP.V1;

/// <summary>One row inside the FAP purchase-item repeater. Persisted as
/// a JSON array on the case row (DB convention rule 6).</summary>
public sealed record FAP_V1_PurchaseItem(
    string?  Category,   // Hardware | Software
    string?  ItemSpec,
    int      Qty);

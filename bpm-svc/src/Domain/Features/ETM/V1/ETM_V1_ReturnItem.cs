namespace Bpm.Domain.Features.ETM.V1;

/// <summary>One row inside the termination handover return-items
/// repeater (asset / account return). Persisted as JSON.</summary>
public sealed record ETM_V1_ReturnItem(
    string?  AssetType,
    string?  Action,    // Transfer | Disable | Return
    string?  Status);   // Pending | Complete

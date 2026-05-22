namespace Bpm.Domain.Entities.Spec;

/// <summary>
/// Lifecycle of a stored <see cref="SpecBundle"/>. <c>Draft</c> is what the
/// onboarding wizard saves before going live; <c>Pending</c> is uploaded
/// awaiting repro; <c>Installed</c> means the runtime can dispatch
/// instances against it; <c>InstalledUnverified</c> covers the path where
/// the operator chose to install despite a failed reproducibility check;
/// <c>Failed</c> is repro-failed and not installed; <c>SoftDeleted</c> is
/// the tombstone for the Flow Library DELETE endpoint.
/// </summary>
public enum SpecBundleStatus
{
    Draft = 0,
    Pending = 1,
    Installed = 2,
    InstalledUnverified = 3,
    Failed = 4,
    SoftDeleted = 5,
}

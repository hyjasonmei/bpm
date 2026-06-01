namespace Bpm.Domain.Features.VENDOR_EXPENSE.V1;

/// <summary>
/// Workflow status for the VENDOR_EXPENSE V1 flow (採購申請流程).
///
/// Three sequential approval stages — 主管審核 (supervisor) → 採購審定
/// (procurement) → 簽核 (sign) — each gated by a reject branch that
/// loops the case back to the filler as <see cref="ResubmitRequired"/>.
/// The <c>approval_sign</c> stage has no explicit reject edge in the
/// bundle, but its reject button + the on_reject notification's
/// "請修正後重新送件" wording mean a sign-stage reject also bounces back
/// to the submitter for resubmit (chef-baked, see service docs).
/// </summary>
public enum VENDOR_EXPENSE_V1_CaseStatus
{
    PendingSupervisor   = 0,
    PendingProcurement  = 1,
    PendingSign         = 2,
    ResubmitRequired    = 3,
    Completed           = 4,
}

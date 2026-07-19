namespace Bpm.Application.Transfer;

/// <summary>
/// End-user case transfer (轉簽): the current personal-stage assignee (or
/// their accepted delegate) hands the case to another active user. Role-queue
/// stages are not transferable — the whole queue can already act. Admin-side
/// remediation stays on Process Doctor. See
/// docs/superpowers/specs/2026-07-19-case-transfer-design.md.
/// </summary>
public interface ICaseTransferService
{
    Task<TransferResult> TransferAsync(
        string flowCode, Guid caseId, Guid actorUserId, Guid toUserId,
        string? reason, CancellationToken ct = default);

    /// <summary>Active-user picker search (DisplayName / Email like, Take 20).</summary>
    Task<IReadOnlyList<TransferCandidate>> CandidatesAsync(string? q, CancellationToken ct = default);
}

/// <param name="Error">null on success; otherwise one of:
/// unknown_flow / not_found_or_closed / role_stage_not_transferable /
/// no_current_assignee / not_current_assignee / target_not_active /
/// target_is_current / reason_required</param>
public sealed record TransferResult(bool Ok, string? Error = null);

public sealed record TransferCandidate(Guid UserId, string Name, string? Email);

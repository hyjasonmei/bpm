namespace Bpm.Domain.Entities.Transfer;

/// <summary>
/// Append-only record of an end-user case transfer (轉簽): the current
/// personal-stage assignee (or their accepted delegate) handed the case to
/// another active user. Distinct from <see cref="Doctor.DoctorActionLog"/>,
/// which records admin remediation — the two audiences are audited separately.
/// </summary>
public sealed class CaseTransferLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FlowCode { get; set; } = string.Empty;
    public int FlowVersion { get; set; }
    public Guid CaseId { get; set; }

    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }

    /// <summary>Actual actor — differs from <see cref="FromUserId"/> when an
    /// accepted delegate performed the transfer.</summary>
    public Guid OperatorUserId { get; set; }

    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

namespace Bpm.Domain.Entities.Doctor;

/// <summary>
/// Append-only record of a Process Doctor remediation (reassign / batch
/// reassign / cancel). Gives accountability for admin overrides that bypass
/// the per-flow state machine. (Integration into the canonical
/// Admin_AuditEvents ledger is a follow-up; v1 keeps its own table.)
/// </summary>
public sealed class DoctorActionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>reassign | batch_reassign | cancel</summary>
    public string Action { get; set; } = string.Empty;

    public string? FlowCode { get; set; }
    public Guid? CaseId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }

    /// <summary>Rows affected (batch reassign touches many).</summary>
    public int Affected { get; set; }

    public Guid? OperatorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

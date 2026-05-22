namespace Bpm.Admin.Domain.Audit;

/// <summary>
/// Append-only audit row per flowcook-audit spec (seven-column schema).
/// </summary>
public class AuditEvent
{
    public Guid EventId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorPrincipalId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string SourceSystem { get; set; } = "admin";
    public string? Reason { get; set; }
}

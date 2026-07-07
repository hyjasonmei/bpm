using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Attendance;

/// A request to back-fill a missed punch. Approved by the requester's direct
/// manager (or a SYSTEM_ADMIN); approval inserts an AttendancePunch with
/// Source = Correction and links it back via CreatedPunchId.
public sealed class AttendanceCorrection : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public PunchType PunchType { get; set; }
    /// UTC instant the punch should be recorded at.
    public DateTime RequestedPunchAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public CorrectionStatus Status { get; set; } = CorrectionStatus.Pending;
    public DateTime SubmittedAt { get; set; }
    public Guid? DeciderUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public Guid? CreatedPunchId { get; set; }
}

public enum CorrectionStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

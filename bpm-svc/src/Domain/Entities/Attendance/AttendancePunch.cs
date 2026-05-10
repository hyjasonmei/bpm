using Bpm.Domain.Common;

namespace Bpm.Domain.Entities.Attendance;

public sealed class AttendancePunch : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PunchType PunchType { get; set; }
    public DateTime PunchAt { get; set; }
    public DateOnly LocalDate { get; set; }
    public PunchSource Source { get; set; } = PunchSource.Manual;
}

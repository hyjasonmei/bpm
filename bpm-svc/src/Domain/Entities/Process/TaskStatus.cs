namespace Bpm.Domain.Entities.Process;

public enum TaskStatus
{
    Pending = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    Skipped = 5,
}

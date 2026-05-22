namespace Bpm.Domain.Entities.Process;

public enum NodeKind
{
    StartEvent = 1,
    EndEvent = 2,
    UserTask = 3,
    Approval = 4,
    Gateway = 5,
    Notify = 6,
    ServiceTask = 7,
}

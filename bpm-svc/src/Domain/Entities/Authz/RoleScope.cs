namespace Bpm.Domain.Entities.Authz;

public enum RoleScope
{
    System = 1,
    Flow = 2,
}

public enum AssignmentScope
{
    Tenant = 1,
    Flow = 2,
    Step = 3,
}

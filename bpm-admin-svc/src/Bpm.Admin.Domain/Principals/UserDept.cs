namespace Bpm.Admin.Domain.Principals;

public class UserDept
{
    public Guid UserId { get; set; }
    public Guid DeptId { get; set; }
    public bool IsPrimary { get; set; }
}

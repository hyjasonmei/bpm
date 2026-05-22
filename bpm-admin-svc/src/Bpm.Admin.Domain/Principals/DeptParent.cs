namespace Bpm.Admin.Domain.Principals;

public class DeptParent
{
    public Guid DeptId { get; set; }
    public Guid? ParentDeptId { get; set; }
}

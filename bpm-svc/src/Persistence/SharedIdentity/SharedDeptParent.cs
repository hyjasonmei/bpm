namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_DeptParents. Single edge per dept (a department has at
// most one parent). Walk recursively to build the org tree.
public class SharedDeptParent
{
    public Guid DeptId { get; set; }
    public Guid? ParentDeptId { get; set; }
}

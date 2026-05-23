namespace Bpm.Persistence.SharedIdentity;

// Mapping onto Admin_UserDepts. A user can belong to multiple departments
// (兼任 / dual-role / cross-functional). Exactly one row per user should
// have IsPrimary = true; this is the dept used for JWT `dept_code` claim
// and the "primary dept" display in the Home header.
public class SharedUserDept
{
    public Guid UserId { get; set; }
    public Guid DeptId { get; set; }
    public bool IsPrimary { get; set; }
}

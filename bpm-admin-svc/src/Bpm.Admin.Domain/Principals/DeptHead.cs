namespace Bpm.Admin.Domain.Principals;

// "This department's head is that user." At most one row per department;
// absence means the dept has no head set. HeadUserId references a Principal
// of Type=User.
//
// ActorRef.dept_head spec actor types resolve via:
//   1. Submitter's primary Admin_UserDepts row → DeptId
//   2. Admin_DeptHeads where DeptId matches → HeadUserId
//
// If no row, the resolver returns empty and the runtime falls back to
// whatever the spec's actor fallback chain says (e.g. role-based, or
// fail-loud).
public class DeptHead
{
    public Guid DeptId { get; set; }
    public Guid HeadUserId { get; set; }
    public DateTime AssignedAt { get; set; }
}

namespace Bpm.Admin.Domain.Principals;

// Direct "this user reports to that user" edge. One row per user that has
// a manager; absence means the user has no manager set (top of the tree,
// or not yet wired). ManagerUserId references another Principal of Type=User.
//
// Why a side table rather than a column on Principal: Principal is the TPT
// base for Users / Departments / Groups; ManagerId is only meaningful for
// Users, and a side table keeps the base entity clean. Mirrors the same
// shape as UserCredentials and UserDept.
public class UserManager
{
    public Guid UserId { get; set; }
    public Guid ManagerUserId { get; set; }
    public DateTime AssignedAt { get; set; }
}

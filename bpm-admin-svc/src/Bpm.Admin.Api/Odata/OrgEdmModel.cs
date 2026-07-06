using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;

namespace Bpm.Admin.Api.Odata;

/// The EDM (entity data model) for the /odata integration surface.
public static class OrgEdmModel
{
    public static IEdmModel Build()
    {
        var b = new ODataConventionModelBuilder();

        var users = b.EntitySet<OrgUser>("Users");
        users.EntityType.HasKey(u => u.Id);
        // Password is set out-of-band via a bound action, never as an entity
        // property — so it stays off reads and $metadata's User properties.
        var setPassword = users.EntityType.Action("SetPassword");
        setPassword.Parameter<string>("password").Required();

        var depts = b.EntitySet<OrgDepartment>("Departments");
        depts.EntityType.HasKey(d => d.Id);

        var groups = b.EntitySet<OrgGroup>("Groups");
        groups.EntityType.HasKey(g => g.Id);

        var groupMembers = b.EntitySet<OrgGroupMember>("GroupMembers");
        groupMembers.EntityType.HasKey(m => m.GroupId);
        groupMembers.EntityType.HasKey(m => m.MemberPrincipalId);

        var roles = b.EntitySet<OrgRole>("Roles");
        roles.EntityType.HasKey(r => r.Id);

        var memberships = b.EntitySet<OrgMembership>("Memberships");
        memberships.EntityType.HasKey(m => m.PrincipalId);
        memberships.EntityType.HasKey(m => m.RoleId);

        var userDepts = b.EntitySet<OrgUserDepartment>("UserDepartments");
        // The convention builder alphabetizes composite keys to (DeptId,
        // UserId) no matter how they're declared, and keyed-URL matching is
        // order-sensitive — clients must write (DeptId=…,UserId=…). The other
        // two composite sets (GroupMembers, Memberships) don't hit this
        // because their natural order happens to be alphabetical already.
        userDepts.EntityType.HasKey(ud => new { ud.UserId, ud.DeptId });

        var managers = b.EntitySet<OrgManager>("Managers");
        managers.EntityType.HasKey(m => m.UserId);

        var deptHeads = b.EntitySet<OrgDepartmentHead>("DepartmentHeads");
        deptHeads.EntityType.HasKey(h => h.DeptId);

        var deptParents = b.EntitySet<OrgDepartmentParent>("DepartmentParents");
        deptParents.EntityType.HasKey(p => p.DeptId);

        return b.GetEdmModel();
    }
}

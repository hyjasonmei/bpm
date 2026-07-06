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

        return b.GetEdmModel();
    }
}

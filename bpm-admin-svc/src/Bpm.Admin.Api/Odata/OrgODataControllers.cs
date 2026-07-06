using Bpm.Admin.Application.Audit;
using Bpm.Admin.Application.Auth;
using Bpm.Admin.Domain.Auth;
using Bpm.Admin.Domain.Principals;
using Bpm.Admin.Domain.Roles;
using Bpm.Admin.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Bpm.Admin.Api.Odata;

// Read/write OData surface for system integration. Basic-auth (dedicated
// integration credential). Writes map to canonical entities honoring existing
// invariants (uniqueness, soft-delete, audit) — never a raw table dump.

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class UsersController(AdminDbContext db, IPasswordHasher hasher, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgUser> Query() => db.Principals.AsNoTracking()
        .Where(p => p.Type == PrincipalType.User && p.DeletedAt == null)
        .Select(p => new OrgUser { Id = p.Id, DisplayName = p.DisplayName, Email = p.Email, Active = p.Active });

    [EnableQuery]
    public IQueryable<OrgUser> Get() => Query();

    [EnableQuery]
    public SingleResult<OrgUser> Get([FromRoute] Guid key) => SingleResult.Create(Query().Where(u => u.Id == key));

    public async Task<IActionResult> Post([FromBody] OrgUser model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.DisplayName)) return BadRequest("DisplayName is required.");

        // ?upsert=true → a POST whose email already exists UPDATES that user
        // instead of 400ing, so a customer can re-push their whole org data
        // idempotently. Default (no flag) stays strict create.
        var upsert = Request.Query.TryGetValue("upsert", out var uv) && uv == "true";
        if (!string.IsNullOrWhiteSpace(model.Email))
        {
            var existing = await db.Principals.FirstOrDefaultAsync(
                p => p.Email == model.Email && p.Type == PrincipalType.User && p.DeletedAt == null, ct);
            if (existing is not null)
            {
                if (!upsert) return BadRequest("Email already in use.");
                existing.DisplayName = model.DisplayName.Trim();
                existing.Active = model.Active;
                await db.SaveChangesAsync(ct);
                await audit.LogAsync("upserted", "principal", existing.Id.ToString(), null, null, after: new { existing.DisplayName, existing.Email, existing.Active }, ct: ct);
                return Updated(new OrgUser { Id = existing.Id, DisplayName = existing.DisplayName, Email = existing.Email, Active = existing.Active });
            }
        }

        var p = new Principal { Type = PrincipalType.User, DisplayName = model.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(), Active = true };
        db.Principals.Add(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, p.Email }, ct: ct);
        model.Id = p.Id; model.Active = p.Active;
        return Created(model);
    }

    // Bound action: POST /odata/Users({key})/SetPassword {"password":"…"}. Sets or
    // resets the login password without ever exposing the credential on the entity.
    [HttpPost]
    public async Task<IActionResult> SetPassword([FromRoute] Guid key, [FromBody] ODataActionParameters parameters, CancellationToken ct)
    {
        if (!await db.Principals.AnyAsync(x => x.Id == key && x.Type == PrincipalType.User && x.DeletedAt == null, ct)) return NotFound();
        if (parameters is null || !parameters.TryGetValue("password", out var pwObj) || pwObj is not string pw || pw.Length < 6)
            return BadRequest("password must be at least 6 characters.");
        var cred = await db.UserCredentials.FirstOrDefaultAsync(c => c.UserId == key, ct);
        if (cred is null) db.UserCredentials.Add(new UserCredential { UserId = key, PasswordHash = hasher.Hash(pw), CreatedAt = DateTime.UtcNow });
        else { cred.PasswordHash = hasher.Hash(pw); cred.PasswordChangedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("password_set", "principal", key.ToString(), null, null, ct: ct);
        return NoContent();
    }

    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<OrgUser> delta, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.User && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        if (delta.TryGetPropertyValue(nameof(OrgUser.DisplayName), out var dn) && dn is string s && !string.IsNullOrWhiteSpace(s)) p.DisplayName = s.Trim();
        if (delta.TryGetPropertyValue(nameof(OrgUser.Email), out var em)) p.Email = em as string;
        if (delta.TryGetPropertyValue(nameof(OrgUser.Active), out var ac) && ac is bool b) p.Active = b;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("updated", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, p.Email, p.Active }, ct: ct);
        return Updated(new OrgUser { Id = p.Id, DisplayName = p.DisplayName, Email = p.Email, Active = p.Active });
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.User && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        p.DeletedAt = DateTime.UtcNow;                       // soft delete, keep history
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "principal", p.Id.ToString(), null, null, ct: ct);
        return NoContent();
    }
}

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class DepartmentsController(AdminDbContext db, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgDepartment> Query() => db.Principals.AsNoTracking()
        .Where(p => p.Type == PrincipalType.Dept && p.DeletedAt == null)
        .Select(p => new OrgDepartment { Id = p.Id, DisplayName = p.DisplayName, Active = p.Active });

    [EnableQuery] public IQueryable<OrgDepartment> Get() => Query();
    [EnableQuery] public SingleResult<OrgDepartment> Get([FromRoute] Guid key) => SingleResult.Create(Query().Where(d => d.Id == key));

    public async Task<IActionResult> Post([FromBody] OrgDepartment model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.DisplayName)) return BadRequest("DisplayName is required.");
        var p = new Principal { Type = PrincipalType.Dept, DisplayName = model.DisplayName.Trim(), Active = true };
        db.Principals.Add(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, kind = "dept" }, ct: ct);
        model.Id = p.Id; model.Active = p.Active;
        return Created(model);
    }

    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<OrgDepartment> delta, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.Dept && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        if (delta.TryGetPropertyValue(nameof(OrgDepartment.DisplayName), out var dn) && dn is string s && !string.IsNullOrWhiteSpace(s)) p.DisplayName = s.Trim();
        if (delta.TryGetPropertyValue(nameof(OrgDepartment.Active), out var ac) && ac is bool b) p.Active = b;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("updated", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, p.Active }, ct: ct);
        return Updated(new OrgDepartment { Id = p.Id, DisplayName = p.DisplayName, Active = p.Active });
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.Dept && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        p.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "principal", p.Id.ToString(), null, null, ct: ct);
        return NoContent();
    }
}

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class RolesController(AdminDbContext db, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgRole> Query() => db.Roles.AsNoTracking()
        .Select(r => new OrgRole { Id = r.Id, Code = r.Code, Name = r.Name, Description = r.Description, IsSystem = r.IsSystem });

    [EnableQuery] public IQueryable<OrgRole> Get() => Query();
    [EnableQuery] public SingleResult<OrgRole> Get([FromRoute] Guid key) => SingleResult.Create(Query().Where(r => r.Id == key));

    public async Task<IActionResult> Post([FromBody] OrgRole model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Code) || string.IsNullOrWhiteSpace(model.Name)) return BadRequest("Code and Name are required.");

        // ?upsert=true → a POST whose code already exists UPDATES that role (idempotent re-push).
        var upsert = Request.Query.TryGetValue("upsert", out var uv) && uv == "true";
        var dup = await db.Roles.FirstOrDefaultAsync(r => r.Code == model.Code, ct);
        if (dup is not null)
        {
            if (!upsert) return BadRequest($"Role code '{model.Code}' already in use.");
            if (dup.IsSystem) return BadRequest("System roles cannot be modified via integration.");
            dup.Name = model.Name.Trim();
            dup.Description = model.Description;
            await db.SaveChangesAsync(ct);
            await audit.LogAsync("upserted", "role", dup.Id.ToString(), null, null, after: new { dup.Code, dup.Name }, ct: ct);
            return Updated(new OrgRole { Id = dup.Id, Code = dup.Code, Name = dup.Name, Description = dup.Description, IsSystem = dup.IsSystem });
        }

        var r = new Role { Id = Guid.NewGuid(), Code = model.Code.Trim(), Name = model.Name.Trim(), Description = model.Description, IsSystem = false };
        db.Roles.Add(r);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "role", r.Id.ToString(), null, null, after: new { r.Code, r.Name }, ct: ct);
        model.Id = r.Id; model.IsSystem = false;
        return Created(model);
    }

    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<OrgRole> delta, CancellationToken ct)
    {
        var r = await db.Roles.FirstOrDefaultAsync(x => x.Id == key, ct);
        if (r is null) return NotFound();
        if (r.IsSystem) return BadRequest("System roles cannot be modified via integration.");
        if (delta.TryGetPropertyValue(nameof(OrgRole.Name), out var nm) && nm is string s && !string.IsNullOrWhiteSpace(s)) r.Name = s.Trim();
        if (delta.TryGetPropertyValue(nameof(OrgRole.Description), out var de)) r.Description = de as string;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("updated", "role", r.Id.ToString(), null, null, after: new { r.Name, r.Description }, ct: ct);
        return Updated(new OrgRole { Id = r.Id, Code = r.Code, Name = r.Name, Description = r.Description, IsSystem = r.IsSystem });
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key, CancellationToken ct)
    {
        var r = await db.Roles.FirstOrDefaultAsync(x => x.Id == key, ct);
        if (r is null) return NotFound();
        if (r.IsSystem) return BadRequest("System roles cannot be deleted via integration.");
        db.PrincipalRoles.RemoveRange(db.PrincipalRoles.Where(pr => pr.RoleId == key));   // clear assignments
        db.Roles.Remove(r);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "role", key.ToString(), null, null, ct: ct);
        return NoContent();
    }
}

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class GroupsController(AdminDbContext db, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgGroup> Query() => db.Principals.AsNoTracking()
        .Where(p => p.Type == PrincipalType.Group && p.DeletedAt == null)
        .Select(p => new OrgGroup { Id = p.Id, DisplayName = p.DisplayName, Active = p.Active });

    [EnableQuery] public IQueryable<OrgGroup> Get() => Query();
    [EnableQuery] public SingleResult<OrgGroup> Get([FromRoute] Guid key) => SingleResult.Create(Query().Where(g => g.Id == key));

    public async Task<IActionResult> Post([FromBody] OrgGroup model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.DisplayName)) return BadRequest("DisplayName is required.");
        var p = new Principal { Type = PrincipalType.Group, DisplayName = model.DisplayName.Trim(), Active = true };
        db.Principals.Add(p);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, kind = "group" }, ct: ct);
        model.Id = p.Id; model.Active = p.Active;
        return Created(model);
    }

    public async Task<IActionResult> Patch([FromRoute] Guid key, [FromBody] Delta<OrgGroup> delta, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.Group && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        if (delta.TryGetPropertyValue(nameof(OrgGroup.DisplayName), out var dn) && dn is string s && !string.IsNullOrWhiteSpace(s)) p.DisplayName = s.Trim();
        if (delta.TryGetPropertyValue(nameof(OrgGroup.Active), out var ac) && ac is bool b) p.Active = b;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("updated", "principal", p.Id.ToString(), null, null, after: new { p.DisplayName, p.Active }, ct: ct);
        return Updated(new OrgGroup { Id = p.Id, DisplayName = p.DisplayName, Active = p.Active });
    }

    public async Task<IActionResult> Delete([FromRoute] Guid key, CancellationToken ct)
    {
        var p = await db.Principals.FirstOrDefaultAsync(x => x.Id == key && x.Type == PrincipalType.Group && x.DeletedAt == null, ct);
        if (p is null) return NotFound();
        p.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "principal", p.Id.ToString(), null, null, ct: ct);
        return NoContent();
    }
}

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class GroupMembersController(AdminDbContext db, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgGroupMember> Query() => db.GroupMembers.AsNoTracking()
        .Select(gm => new OrgGroupMember { GroupId = gm.GroupId, MemberPrincipalId = gm.MemberPrincipalId, MemberType = gm.MemberType.ToString() });

    [EnableQuery] public IQueryable<OrgGroupMember> Get() => Query();

    [EnableQuery]
    public SingleResult<OrgGroupMember> Get([FromRoute] Guid keyGroupId, [FromRoute] Guid keyMemberPrincipalId)
        => SingleResult.Create(Query().Where(m => m.GroupId == keyGroupId && m.MemberPrincipalId == keyMemberPrincipalId));

    public async Task<IActionResult> Post([FromBody] OrgGroupMember model, CancellationToken ct)
    {
        if (model.GroupId == model.MemberPrincipalId) return BadRequest("A group cannot contain itself.");
        var group = await db.Principals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == model.GroupId && p.Type == PrincipalType.Group && p.DeletedAt == null, ct);
        if (group is null) return BadRequest("Group not found.");
        var member = await db.Principals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == model.MemberPrincipalId && p.DeletedAt == null, ct);
        if (member is null) return BadRequest("Member principal not found.");

        var upsert = Request.Query.TryGetValue("upsert", out var uv) && uv == "true";
        var existing = await db.GroupMembers.AsNoTracking()
            .FirstOrDefaultAsync(gm => gm.GroupId == model.GroupId && gm.MemberPrincipalId == model.MemberPrincipalId, ct);
        if (existing is not null)
        {
            // ?upsert=true → re-asserting an existing member is a no-op success (idempotent).
            if (!upsert) return BadRequest("Group member already exists.");
            return Updated(new OrgGroupMember { GroupId = existing.GroupId, MemberPrincipalId = existing.MemberPrincipalId, MemberType = existing.MemberType.ToString() });
        }

        db.GroupMembers.Add(new GroupMember { GroupId = model.GroupId, MemberPrincipalId = model.MemberPrincipalId, MemberType = member.Type });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "group_member", $"{model.GroupId}:{model.MemberPrincipalId}", null, null,
            after: new { model.GroupId, model.MemberPrincipalId, MemberType = member.Type.ToString() }, ct: ct);
        model.MemberType = member.Type.ToString();
        return Created(model);
    }

    public async Task<IActionResult> Delete([FromRoute] Guid keyGroupId, [FromRoute] Guid keyMemberPrincipalId, CancellationToken ct)
    {
        var gm = await db.GroupMembers.FirstOrDefaultAsync(x => x.GroupId == keyGroupId && x.MemberPrincipalId == keyMemberPrincipalId, ct);
        if (gm is null) return NotFound();
        db.GroupMembers.Remove(gm);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "group_member", $"{keyGroupId}:{keyMemberPrincipalId}", null, null, ct: ct);
        return NoContent();
    }
}

[Authorize(AuthenticationSchemes = OdataBasicAuthHandler.SchemeName)]
public sealed class MembershipsController(AdminDbContext db, IAuditLogger audit) : ODataController
{
    private IQueryable<OrgMembership> Query() => db.PrincipalRoles.AsNoTracking()
        .Select(pr => new OrgMembership { PrincipalId = pr.PrincipalId, RoleId = pr.RoleId, InheritToMembers = pr.InheritToMembers, IncludeSubDepts = pr.IncludeSubDepts, AssignedAt = pr.AssignedAt });

    [EnableQuery] public IQueryable<OrgMembership> Get() => Query();

    [EnableQuery]
    public SingleResult<OrgMembership> Get([FromRoute] Guid keyPrincipalId, [FromRoute] Guid keyRoleId)
        => SingleResult.Create(Query().Where(m => m.PrincipalId == keyPrincipalId && m.RoleId == keyRoleId));

    public async Task<IActionResult> Post([FromBody] OrgMembership model, CancellationToken ct)
    {
        if (!await db.Principals.AnyAsync(p => p.Id == model.PrincipalId && p.DeletedAt == null, ct)) return BadRequest("Principal not found.");
        if (!await db.Roles.AnyAsync(r => r.Id == model.RoleId, ct)) return BadRequest("Role not found.");
        var upsert = Request.Query.TryGetValue("upsert", out var uv) && uv == "true";
        var existing = await db.PrincipalRoles.FirstOrDefaultAsync(pr => pr.PrincipalId == model.PrincipalId && pr.RoleId == model.RoleId, ct);
        if (existing is not null)
        {
            // ?upsert=true → re-asserting an existing membership updates its
            // flags (idempotent for the same payload).
            if (!upsert) return BadRequest("Membership already exists.");
            existing.InheritToMembers = model.InheritToMembers;
            existing.IncludeSubDepts = model.IncludeSubDepts;
            await db.SaveChangesAsync(ct);
            return Updated(new OrgMembership { PrincipalId = existing.PrincipalId, RoleId = existing.RoleId, InheritToMembers = existing.InheritToMembers, IncludeSubDepts = existing.IncludeSubDepts, AssignedAt = existing.AssignedAt });
        }
        db.PrincipalRoles.Add(new PrincipalRole { PrincipalId = model.PrincipalId, RoleId = model.RoleId, InheritToMembers = model.InheritToMembers, IncludeSubDepts = model.IncludeSubDepts, AssignedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("created", "principal_role", $"{model.PrincipalId}:{model.RoleId}", null, null, after: new { model.PrincipalId, model.RoleId }, ct: ct);
        model.AssignedAt = DateTime.UtcNow;
        return Created(model);
    }

    public async Task<IActionResult> Delete([FromRoute] Guid keyPrincipalId, [FromRoute] Guid keyRoleId, CancellationToken ct)
    {
        var pr = await db.PrincipalRoles.FirstOrDefaultAsync(x => x.PrincipalId == keyPrincipalId && x.RoleId == keyRoleId, ct);
        if (pr is null) return NotFound();
        db.PrincipalRoles.Remove(pr);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("deleted", "principal_role", $"{keyPrincipalId}:{keyRoleId}", null, null, ct: ct);
        return NoContent();
    }
}

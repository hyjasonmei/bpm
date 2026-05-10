using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Seed;

/// Idempotent seed creating a small org chart and the system roles needed
/// for persona login. Re-running on a populated DB inserts nothing.
public static class OrgFixture
{
    public static async Task RunAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("OrgFixture: Users already exist, skipping main seed");
            await EnsureHrRoleAsync(db, logger, ct);
            return;
        }

        // Departments — 2-level tree
        // CORP (root)
        //   ├ ENG
        //   └ FIN
        var corp = new Department { Id = Guid.NewGuid(), DisplayName = "Corporate", Code = "CORP", Name = "Corporate" };
        var eng  = new Department { Id = Guid.NewGuid(), DisplayName = "Engineering", Code = "ENG", Name = "Engineering", ParentId = corp.Id };
        var fin  = new Department { Id = Guid.NewGuid(), DisplayName = "Finance", Code = "FIN", Name = "Finance", ParentId = corp.Id };

        // Users — IDs are stable so persona mapping by email works deterministically
        var ceo     = new User { Id = Guid.NewGuid(), DisplayName = "Sandy Chen",    Email = "ceo@bpm.local",     FullName = "Sandy Chen (CEO)",        DepartmentId = corp.Id };
        var elton   = new User { Id = Guid.NewGuid(), DisplayName = "Elton Yang",   Email = "manager@bpm.local", FullName = "Elton Yang (Manager)",     DepartmentId = eng.Id, ManagerId = ceo.Id };
        var wilson  = new User { Id = Guid.NewGuid(), DisplayName = "Wilson You",   Email = "employee@bpm.local",FullName = "Wilson You (Employee)",    DepartmentId = eng.Id, ManagerId = elton.Id };
        var jean    = new User { Id = Guid.NewGuid(), DisplayName = "Jean Hsu",     Email = "finance@bpm.local", FullName = "Jean Hsu (Finance)",       DepartmentId = fin.Id, ManagerId = ceo.Id };
        var mark    = new User { Id = Guid.NewGuid(), DisplayName = "Mark Ng",      Email = "it@bpm.local",      FullName = "Mark Ng (IT)",             DepartmentId = eng.Id, ManagerId = elton.Id };
        var amy     = new User { Id = Guid.NewGuid(), DisplayName = "Amy Lin",      Email = "hr@bpm.local",      FullName = "Amy Lin (HR)",             DepartmentId = corp.Id, ManagerId = ceo.Id };
        var admin   = new User { Id = Guid.NewGuid(), DisplayName = "System Admin", Email = "admin@bpm.local",   FullName = "System Admin",             DepartmentId = corp.Id };
        var extraEng = new User { Id = Guid.NewGuid(), DisplayName = "Bob Engineer", Email = "bob@bpm.local",     FullName = "Bob Engineer",             DepartmentId = eng.Id, ManagerId = elton.Id };
        var extraFin = new User { Id = Guid.NewGuid(), DisplayName = "Cathy Finance",Email = "cathy@bpm.local",   FullName = "Cathy Finance",            DepartmentId = fin.Id, ManagerId = jean.Id };
        var extraOps = new User { Id = Guid.NewGuid(), DisplayName = "Dan Ops",      Email = "dan@bpm.local",     FullName = "Dan Ops",                  DepartmentId = corp.Id, ManagerId = ceo.Id };

        // Insert Departments (no head yet) + Users + Groups + Roles in one batch.
        // HeadUserId is filled in a second SaveChanges to break the circular FK
        // (Department.HeadUserId → User → DepartmentId → Department).
        await db.Departments.AddRangeAsync([corp, eng, fin], ct);
        await db.Users.AddRangeAsync([ceo, elton, wilson, jean, mark, amy, admin, extraEng, extraFin, extraOps], ct);

        var financeReviewers = new Group { Id = Guid.NewGuid(), DisplayName = "Finance Reviewers", Code = "FIN_REVIEWERS", Name = "Finance Reviewers", Description = "Anyone who can run financial review on cases" };
        var procurementCommittee = new Group { Id = Guid.NewGuid(), DisplayName = "Procurement Committee", Code = "PROC_COMMITTEE", Name = "Procurement Committee", Description = "Committee for high-value procurement" };

        var roleAdmin    = new Role { Id = Guid.NewGuid(), Code = "admin",    Name = "System Administrator", Scope = RoleScope.System };
        var roleDesigner = new Role { Id = Guid.NewGuid(), Code = "designer", Name = "Spec Designer",        Scope = RoleScope.System };
        var roleViewer   = new Role { Id = Guid.NewGuid(), Code = "viewer",   Name = "Read-only Viewer",     Scope = RoleScope.System };
        var roleHr       = new Role { Id = Guid.NewGuid(), Code = "hr",       Name = "Human Resources",      Scope = RoleScope.System };

        await db.Groups.AddRangeAsync([financeReviewers, procurementCommittee], ct);
        await db.Roles.AddRangeAsync([roleAdmin, roleDesigner, roleViewer, roleHr], ct);

        await db.SaveChangesAsync(ct);

        // Phase 2: now that Users + Departments exist, fill in the heads.
        corp.HeadUserId = ceo.Id;
        eng.HeadUserId = elton.Id;
        fin.HeadUserId = jean.Id;

        // Group memberships
        await db.GroupMembers.AddRangeAsync([
            new GroupMember { GroupId = financeReviewers.Id, PrincipalId = jean.Id },
            new GroupMember { GroupId = financeReviewers.Id, PrincipalId = extraFin.Id },
            new GroupMember { GroupId = procurementCommittee.Id, PrincipalId = ceo.Id },
            new GroupMember { GroupId = procurementCommittee.Id, PrincipalId = jean.Id },
            new GroupMember { GroupId = procurementCommittee.Id, PrincipalId = elton.Id },
        ], ct);

        // Role assignments — admin persona gets admin role, designer to manager+IT, viewer to everyone
        await db.RoleAssignments.AddRangeAsync([
            new RoleAssignment { RoleId = roleAdmin.Id,    PrincipalId = admin.Id },
            new RoleAssignment { RoleId = roleAdmin.Id,    PrincipalId = ceo.Id },
            new RoleAssignment { RoleId = roleDesigner.Id, PrincipalId = elton.Id },
            new RoleAssignment { RoleId = roleDesigner.Id, PrincipalId = mark.Id },
            new RoleAssignment { RoleId = roleDesigner.Id, PrincipalId = admin.Id },
            new RoleAssignment { RoleId = roleViewer.Id,   PrincipalId = wilson.Id },
            new RoleAssignment { RoleId = roleViewer.Id,   PrincipalId = jean.Id },
            new RoleAssignment { RoleId = roleViewer.Id,   PrincipalId = amy.Id },
            new RoleAssignment { RoleId = roleHr.Id,       PrincipalId = amy.Id },
        ], ct);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("OrgFixture: seeded {Users} users, {Depts} departments, {Groups} groups, {Roles} roles, {Assignments} role assignments",
            10, 3, 2, 4, 9);
    }

    // Backfill for DBs seeded before the `hr` role was added. Idempotent.
    private static async Task EnsureHrRoleAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var hrRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == "hr", ct);
        if (hrRole is null)
        {
            hrRole = new Role { Id = Guid.NewGuid(), Code = "hr", Name = "Human Resources", Scope = RoleScope.System };
            db.Roles.Add(hrRole);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("OrgFixture: backfilled missing hr role");
        }

        var amy = await db.Users.FirstOrDefaultAsync(u => u.Email == "hr@bpm.local", ct);
        if (amy is null) return;

        var hasAssignment = await db.RoleAssignments.AnyAsync(ra => ra.RoleId == hrRole.Id && ra.PrincipalId == amy.Id, ct);
        if (!hasAssignment)
        {
            db.RoleAssignments.Add(new RoleAssignment { RoleId = hrRole.Id, PrincipalId = amy.Id });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("OrgFixture: backfilled hr role assignment to amy");
        }
    }
}

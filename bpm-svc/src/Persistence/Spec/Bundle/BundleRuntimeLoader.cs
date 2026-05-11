using Bpm.Application.Spec.Bundle;
using Bpm.Domain.Entities.Authz;
using Bpm.Domain.Entities.Org;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Spec.Bundle;

/// <summary>
/// Reference implementation of <see cref="IBundleRuntimeLoader"/>.
/// Materialises the bundled <c>SampleOrg</c> snapshot under a derived
/// scratch tenant code, registers no spec on the live loader (the
/// runtime takes spec text inline via the new
/// <c>IProcessRuntime.StartInstanceAsync(cmd, inlineSpecJson, ct)</c>
/// overload), and returns a handle whose dispose tears the scratch
/// rows back out.
///
/// <para>
/// Org-side entities (<c>Principal</c> / <c>User</c> / <c>Department</c>
/// / <c>Group</c>) don't carry a per-row tenant column today, so the
/// loader namespaces them by:
///   - rewriting <c>User.Email</c> to a <c>{scratch-tenant}__{original}</c>
///     form so the unique-Email index still holds across loads, and
///   - tracking every inserted row id on the handle so dispose-time
///     cleanup can target exactly the rows this load created.
/// Process-side entities (<c>ProcessInstance</c> / <c>ProcessTask</c> /
/// <c>TaskHistory</c>) do carry <c>TenantCode</c>, so cleanup deletes
/// them by tenant filter — much cheaper than tracking per-instance ids.
/// </para>
///
/// <para>
/// Idempotent: a second load of the same parsed bundle short-circuits
/// to the existing scratch rather than re-seeding. Lets the
/// reproducibility runner be retried mid-flight without leaving stale
/// duplicates behind.
/// </para>
/// </summary>
public sealed class BundleRuntimeLoader(
    AppDbContext db,
    ILogger<BundleRuntimeLoader> logger) : IBundleRuntimeLoader
{
    /// <summary>
    /// All scratch tenants share this prefix. The dispose-time cleanup
    /// asserts the prefix is present before deleting rows — defence
    /// against a misconfigured caller passing a real tenant code in.
    /// </summary>
    public const string ScratchTenantPrefix = "repro-";

    public async Task<LoadedBundleHandle> LoadAsync(ParsedBundle b, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(b);

        var checksum = b.ManifestChecksum ?? string.Empty;
        var checksumShort = checksum.AsSpan(0, Math.Min(8, checksum.Length)).ToString().ToLowerInvariant();
        var flowCodeLower = (b.Manifest?.FlowCode ?? "unknown").ToLowerInvariant();
        var scratchTenant = $"{ScratchTenantPrefix}{flowCodeLower}-{checksumShort}";
        var emailPrefix = $"{scratchTenant}__";

        var existingUserIds = await db.Users
            .AsNoTracking()
            .Where(u => u.Email.StartsWith(emailPrefix))
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (existingUserIds.Count > 0)
        {
            // Same checksum already seeded — re-attach a handle pointing at
            // the live rows. Avoid a second insert pass that would race the
            // unique-Email index. Synthesized roles are intentionally
            // shared across reloads — the role-cleanup pass on dispose
            // only runs against the most recent seed's role-id list.
            logger.LogInformation(
                "BundleRuntimeLoader: scratch tenant {Tenant} already seeded ({Users} users); reusing",
                scratchTenant, existingUserIds.Count);

            var existingDeptIds = b.SampleOrg.Departments.Select(d => d.Id).ToList();
            var existingGroupIds = b.SampleOrg.Groups.Select(g => g.Id).ToList();
            var initiator = await PickInitiatorAsync(emailPrefix, ct);
            return BuildHandle(b, scratchTenant, existingUserIds.Count, initiator,
                existingUserIds, existingDeptIds, existingGroupIds, Array.Empty<Guid>());
        }

        var (seededUserIds, seededDeptIds, seededGroupIds, seededRoleIds) =
            await SeedAsync(b, scratchTenant, emailPrefix, ct);
        var initiatorId = await PickInitiatorAsync(emailPrefix, ct);

        logger.LogInformation(
            "BundleRuntimeLoader: seeded scratch tenant {Tenant} with {Users} users, {Roles} synth roles",
            scratchTenant, seededUserIds.Count, seededRoleIds.Count);

        return BuildHandle(b, scratchTenant, seededUserIds.Count, initiatorId,
            seededUserIds, seededDeptIds, seededGroupIds, seededRoleIds);
    }

    private LoadedBundleHandle BuildHandle(
        ParsedBundle b,
        string scratchTenant,
        int seededUsers,
        Guid initiatorId,
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<Guid> deptIds,
        IReadOnlyList<Guid> groupIds,
        IReadOnlyList<Guid> roleIds)
    {
        return new LoadedBundleHandle
        {
            ScratchTenantCode = scratchTenant,
            RegisteredSpecCode = b.Manifest?.FlowCode ?? string.Empty,
            SeededUserCount = seededUsers,
            SpecJson = b.SpecJson.GetRawText(),
            InitiatorUserId = initiatorId,
            OnDispose = () => CleanupAsync(scratchTenant, userIds, deptIds, groupIds, roleIds),
        };
    }

    private async Task<(List<Guid> Users, List<Guid> Depts, List<Guid> Groups, List<Guid> Roles)> SeedAsync(
        ParsedBundle b, string scratchTenant, string emailPrefix, CancellationToken ct)
    {
        var org = b.SampleOrg;

        var deptIds = new List<Guid>(org.Departments.Count);
        var userIds = new List<Guid>(org.Users.Count);
        var groupIds = new List<Guid>(org.Groups.Count);
        var synthesizedRoleIds = new List<Guid>();

        // Departments first — User.DepartmentId may FK back; insert order
        // matters because EF would otherwise need to defer those FKs.
        foreach (var d in org.Departments)
        {
            db.Departments.Add(new Department
            {
                Id = d.Id,
                Code = d.Code,
                Name = d.Name,
                ParentId = d.ParentId,
                HeadUserId = d.HeadUserId,
                DisplayName = d.Name,
            });
            deptIds.Add(d.Id);
        }

        foreach (var u in org.Users)
        {
            db.Users.Add(new User
            {
                Id = u.Id,
                // Namespace the email so the unique-Email index doesn't
                // collide with rows from a previous load (same Guid would
                // already have been wiped) or — more importantly — with
                // the host tenant's real users. The repro runner doesn't
                // use Email for anything, so the rewrite is invisible.
                Email = $"{emailPrefix}{u.Email}",
                FullName = u.FullName,
                ManagerId = u.ManagerId,
                DepartmentId = u.DepartmentId,
                DisplayName = u.FullName,
                IsActive = true,
            });
            userIds.Add(u.Id);
        }

        foreach (var g in org.Groups)
        {
            db.Groups.Add(new Group
            {
                Id = g.Id,
                Code = $"{scratchTenant}__{g.Code}",
                Name = g.Name,
                DisplayName = g.Name,
            });
            groupIds.Add(g.Id);

            foreach (var memberId in g.MemberPrincipalIds)
            {
                db.GroupMembers.Add(new GroupMember
                {
                    GroupId = g.Id,
                    PrincipalId = memberId,
                });
            }
        }

        // Roles + RoleAssignments. Roles aren't tenant-scoped in v1, so look
        // up by code first and create on the fly when missing. Tagging the
        // synthesized Role with FlowCode = scratch-tenant keeps it
        // traceable back to the load that introduced it.
        var roleAssignmentIds = new List<Guid>();
        foreach (var ra in org.RoleAssignments)
        {
            // Match the runtime's role-lookup rule: ResolveRole requires
            // Scope=System OR (Scope=Flow AND FlowCode == instance.SpecCode).
            // We synthesize Flow-scoped roles tagged with the bundle's
            // FlowCode so the assignment is visible only when running this
            // bundle's spec — never leaks into other tenants' flows.
            var bundleFlowCode = b.Manifest?.FlowCode ?? string.Empty;
            var role = await db.Roles.FirstOrDefaultAsync(r =>
                r.Code == ra.RoleCode
                && (r.Scope == RoleScope.System
                    || (r.Scope == RoleScope.Flow && r.FlowCode == bundleFlowCode)), ct);
            if (role is null)
            {
                role = new Role
                {
                    Id = Guid.NewGuid(),
                    Code = ra.RoleCode,
                    Name = ra.RoleCode,
                    Scope = RoleScope.Flow,
                    FlowCode = bundleFlowCode,
                };
                db.Roles.Add(role);
                await db.SaveChangesAsync(ct);
                synthesizedRoleIds.Add(role.Id);
            }

            var raRow = new RoleAssignment
            {
                Id = Guid.NewGuid(),
                RoleId = role.Id,
                PrincipalId = ra.PrincipalId,
                Scope = ra.Scope == "tenant" ? AssignmentScope.Tenant : AssignmentScope.Flow,
                ScopeRef = ra.ScopeRef,
            };
            db.RoleAssignments.Add(raRow);
            roleAssignmentIds.Add(raRow.Id);
        }

        await db.SaveChangesAsync(ct);
        return (userIds, deptIds, groupIds, synthesizedRoleIds);
    }

    private async Task<Guid> PickInitiatorAsync(string emailPrefix, CancellationToken ct)
    {
        // The repro runner needs *some* user to start the instance as.
        // Prefer a user with a non-null ManagerId so flows that resolve
        // submitter.manager have a real target — falling back to the
        // lowest-id user means a flow whose first approver is the
        // initiator's own manager would always Conflict at start. Order
        // by Id within each tier for stability across runs.
        var withMgr = await db.Users
            .AsNoTracking()
            .Where(x => x.Email.StartsWith(emailPrefix) && x.ManagerId != null)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
        if (withMgr != Guid.Empty) return withMgr;

        return await db.Users
            .AsNoTracking()
            .Where(x => x.Email.StartsWith(emailPrefix))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async ValueTask CleanupAsync(
        string scratchTenant,
        IReadOnlyList<Guid> userIds,
        IReadOnlyList<Guid> deptIds,
        IReadOnlyList<Guid> groupIds,
        IReadOnlyList<Guid> synthesizedRoleIds)
    {
        AssertScratchTenant(scratchTenant);

        // Process-side rows DO carry TenantCode — drop them with one
        // tenant-scoped statement each. ExecuteDeleteAsync bypasses the
        // AuditSaveChangesInterceptor's append-only guards on TaskHistory
        // (the only way to remove those rows from a scratch namespace).
        await db.TaskHistory.Where(h => h.TenantCode == scratchTenant).ExecuteDeleteAsync();
        await db.ProcessTasks.Where(t => t.TenantCode == scratchTenant).ExecuteDeleteAsync();
        await db.ProcessInstances.Where(i => i.TenantCode == scratchTenant).ExecuteDeleteAsync();

        // Org-side rows do NOT carry a tenant column today (Principal /
        // User / Department / Group are tenant-shared in the v1 schema).
        // We track the ids the load inserted and target those exactly.
        //
        // TPT mapping (User / Department / Group all derive from
        // Principal) means EF's ExecuteDelete refuses to translate against
        // the derived DbSets. We delete via the Principal base — Sqlite's
        // ON DELETE CASCADE removes the matching rows from the
        // Users / Departments / Groups child tables.
        if (groupIds.Count > 0)
        {
            await db.GroupMembers.Where(m => groupIds.Contains(m.GroupId)).ExecuteDeleteAsync();
        }
        if (userIds.Count > 0)
        {
            await db.GroupMembers.Where(m => userIds.Contains(m.PrincipalId)).ExecuteDeleteAsync();
            await db.RoleAssignments.Where(ra => userIds.Contains(ra.PrincipalId)).ExecuteDeleteAsync();
        }

        // EF Core 10 forbids ExecuteDelete on TPT-mapped hierarchies
        // (Principal / User / Department / Group), so we walk through
        // tracked entities and Remove them. The bundle org is small enough
        // that load + Remove is cheap, and it keeps us off raw SQL (the
        // root CLAUDE.md bans raw SQL for cross-DB portability).
        //
        // Step 1: clear self-referencing FKs that have ON DELETE RESTRICT
        // (User.ManagerId → Users, Department.HeadUserId → Users,
        // Department.ParentId → Departments). Without this the
        // subsequent removes are rejected by SQLite's FK enforcement.
        if (userIds.Count > 0)
        {
            var trackedUsers = await db.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync();
            foreach (var u in trackedUsers) u.ManagerId = null;
            if (trackedUsers.Count > 0) await db.SaveChangesAsync();
        }
        if (deptIds.Count > 0)
        {
            var trackedDepts = await db.Departments
                .Where(d => deptIds.Contains(d.Id))
                .ToListAsync();
            foreach (var d in trackedDepts)
            {
                d.HeadUserId = null;
                d.ParentId = null;
            }
            if (trackedDepts.Count > 0) await db.SaveChangesAsync();
        }

        // Step 2: detach everything currently tracked, then remove via the
        // base Principal DbSet. The Departments/Users/Groups child tables
        // are wired with FK_*_Principals_Id ON DELETE CASCADE, so SQLite
        // drops the child rows automatically when the principal row goes.
        // Removing through the base type sidesteps EF's TPT-aware delete
        // (which would emit two DELETEs, racing with the cascade).
        db.ChangeTracker.Clear();

        var allPrincipalIds = userIds.Concat(deptIds).Concat(groupIds).ToList();
        if (allPrincipalIds.Count > 0)
        {
            var principals = await db.Principals
                .Where(p => allPrincipalIds.Contains(p.Id))
                .ToListAsync();
            db.Principals.RemoveRange(principals);
            await db.SaveChangesAsync();
        }

        // Drop synthesized roles last — RoleAssignments referencing them
        // were already removed above, so the cascade is clean.
        if (synthesizedRoleIds.Count > 0)
        {
            await db.Roles.Where(r => synthesizedRoleIds.Contains(r.Id)).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// Defensive guard against deleting non-scratch data. Public for unit
    /// testing — see <c>BundleRuntimeLoaderTests.Cleanup_refuses_real_tenant</c>.
    /// </summary>
    public static void AssertScratchTenant(string tenantCode)
    {
        if (tenantCode is null
            || !tenantCode.StartsWith(ScratchTenantPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"BundleRuntimeLoader cleanup refused: tenant '{tenantCode}' is not a scratch tenant " +
                $"(must start with '{ScratchTenantPrefix}').");
        }
    }
}

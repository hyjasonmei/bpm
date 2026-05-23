using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Spec;
using Bpm.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.SeedCli.Commands;

/// <summary>
/// Quick health snapshot of the DB. Counts rows across the entities the
/// dev / sales workflows touch most often (persona seed, runtime,
/// bundles, sandbox state).
/// </summary>
public static class StatusCommand
{
    public static async Task<int> RunAsync(
        string? sqliteFilePath,
        AppDbContext db,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await db.Database.MigrateAsync(ct);

            var migrations = (await db.Database.GetAppliedMigrationsAsync(ct)).Count();

            // Identity counts after unify-user-store: read from the
            // SharedX entity mappings onto Admin_* tables.
            var users = await db.SharedPrincipals
                .CountAsync(p => p.Type == SharedIdentity.SharedPrincipalType.User && p.DeletedAt == null, ct);
            var depts = await db.SharedPrincipals
                .CountAsync(p => p.Type == SharedIdentity.SharedPrincipalType.Dept && p.DeletedAt == null, ct);
            var roles = await db.SharedRoles.CountAsync(ct);
            var roleAssignments = await db.SharedPrincipalRoles.CountAsync(ct);

            var instActive = await db.ProcessInstances.CountAsync(i => i.Status == InstanceStatus.Running, ct);
            var instCompleted = await db.ProcessInstances.CountAsync(i => i.Status == InstanceStatus.Completed, ct);
            var instCancelled = await db.ProcessInstances.CountAsync(i => i.Status == InstanceStatus.Cancelled, ct);
            var instErrored = await db.ProcessInstances.CountAsync(i => i.Status == InstanceStatus.Errored, ct);

            var bundleInstalled = await db.SpecBundles.CountAsync(b => b.Status == SpecBundleStatus.Installed, ct);
            var bundlePending = await db.SpecBundles.CountAsync(b => b.Status == SpecBundleStatus.Pending, ct);
            var bundleFailed = await db.SpecBundles.CountAsync(b => b.Status == SpecBundleStatus.Failed, ct);
            var bundleDraft = await db.SpecBundles.CountAsync(b => b.Status == SpecBundleStatus.Draft, ct);

            var sandbox = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            // Plain stdout so the format stays readable when copied into
            // demos / Slack. Avoids structured logger formatting which
            // injects timestamps + scopes.
            Console.WriteLine();
            Console.WriteLine($"DB:                {sqliteFilePath ?? "(unknown — check ConnectionStrings:Default)"}");
            Console.WriteLine($"Migrations:        {migrations} applied");
            Console.WriteLine($"Users:             {users}");
            Console.WriteLine($"Departments:       {depts}");
            Console.WriteLine($"Roles:             {roles}");
            Console.WriteLine($"RoleAssignments:   {roleAssignments}");
            Console.WriteLine($"ProcessInstances:  {instActive} active / {instCompleted} completed / {instCancelled} cancelled / {instErrored} errored");
            Console.WriteLine($"SpecBundles:       {bundleInstalled} installed / {bundlePending} pending / {bundleFailed} failed / {bundleDraft} draft");
            if (sandbox is not null)
            {
                var modeText = sandbox.SandboxMode ? "on" : "off";
                var offsetText = sandbox.SandboxClockOffsetSeconds == 0
                    ? "0s"
                    : TimeSpan.FromSeconds(sandbox.SandboxClockOffsetSeconds).ToString();
                Console.WriteLine($"SandboxMode:       {modeText} (clock offset: {offsetText})");
            }
            else
            {
                Console.WriteLine("SandboxMode:       (no TenantSettings row)");
            }
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FAIL] status failed: {Message}", ex.Message);
            return 1;
        }
    }
}

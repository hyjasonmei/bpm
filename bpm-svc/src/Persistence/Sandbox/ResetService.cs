using Bpm.Application.Common.Exceptions;
using Bpm.Application.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bpm.Persistence.Sandbox;

/// <summary>
/// PR-J4 §5: hard-deletes sandbox process state. Uses <c>ExecuteDeleteAsync</c>
/// which bypasses <c>AuditSaveChangesInterceptor</c> entirely (the only way
/// to remove TaskHistory rows, which the interceptor otherwise treats as
/// append-only). Pure EF Core — no raw SQL — so the same code runs on
/// SQLite (POC) and Postgres (prod) per the project DB conventions.
///
/// Scope decisions:
/// <list type="bullet">
///   <item>v1 single-tenant POC — both methods scope to <c>"default"</c>
///   tenant. Multi-tenancy lands later and will swap this for an injected
///   tenant context.</item>
///   <item>Audit table for reset events is intentionally skipped (Info log
///   only) — same approach the clock service took for §4.8.</item>
///   <item>Sandbox-on check uses the existing <c>ISandboxClockService.GetAsync</c>
///   so we don't duplicate the TenantSettings load logic.</item>
/// </list>
/// </summary>
public sealed class ResetService(
    AppDbContext db,
    ISandboxClockService clockService,
    ILogger<ResetService> logger) : IResetService
{
    private const string DefaultTenant = "default";

    public async Task<ResetSummary> ResetInstanceAsync(Guid instanceId, Guid actorUserId, CancellationToken ct = default)
    {
        await EnsureSandboxOnAsync(ct);

        // Order: history → tasks → instance → captured. SQLite without enforced
        // FKs doesn't strictly require this but Postgres will, so we follow
        // child-before-parent throughout. Captured rows reference the instance
        // by id but the FK is nullable, so they sit at the end.
        var historyDeleted = await db.TaskHistory
            .Where(h => h.ProcessInstanceId == instanceId)
            .ExecuteDeleteAsync(ct);
        var tasksDeleted = await db.ProcessTasks
            .Where(t => t.ProcessInstanceId == instanceId)
            .ExecuteDeleteAsync(ct);
        var instancesDeleted = await db.ProcessInstances
            .Where(i => i.Id == instanceId)
            .ExecuteDeleteAsync(ct);
        var capturedDeleted = await db.SandboxCapturedMessages
            .Where(m => m.ProcessInstanceId == instanceId)
            .ExecuteDeleteAsync(ct);

        var summary = new ResetSummary(instancesDeleted, tasksDeleted, historyDeleted, capturedDeleted);
        logger.LogInformation(
            "Sandbox reset instance {InstanceId} by {Actor}: deleted {Instances} instance, {Tasks} tasks, {History} history, {Captured} captured",
            instanceId, actorUserId, summary.InstancesDeleted, summary.TasksDeleted,
            summary.HistoryRowsDeleted, summary.CapturedMessagesDeleted);
        return summary;
    }

    public async Task<ResetSummary> ResetAllAsync(Guid actorUserId, CancellationToken ct = default)
    {
        await EnsureSandboxOnAsync(ct);

        // Tenant-scope every delete so spec / org / bundle / tenant-settings rows
        // survive untouched. ExecuteDeleteAsync bypasses interceptors so the
        // append-only TaskHistory guard doesn't fire.
        var historyDeleted = await db.TaskHistory
            .Where(h => h.TenantCode == DefaultTenant)
            .ExecuteDeleteAsync(ct);
        var tasksDeleted = await db.ProcessTasks
            .Where(t => t.TenantCode == DefaultTenant)
            .ExecuteDeleteAsync(ct);
        var instancesDeleted = await db.ProcessInstances
            .Where(i => i.TenantCode == DefaultTenant)
            .ExecuteDeleteAsync(ct);
        var capturedDeleted = await db.SandboxCapturedMessages
            .Where(m => m.TenantCode == DefaultTenant)
            .ExecuteDeleteAsync(ct);

        // Reset clock offset too — "reset everything" means tester gets a
        // clean slate AND wall-clock alignment back. Doesn't touch the
        // sandbox-on toggle itself (the tester wants to keep testing).
        await clockService.ResetAsync(ct);

        var summary = new ResetSummary(instancesDeleted, tasksDeleted, historyDeleted, capturedDeleted);
        logger.LogInformation(
            "Sandbox reset ALL by {Actor}: deleted {Instances} instances, {Tasks} tasks, {History} history, {Captured} captured (clock offset cleared)",
            actorUserId, summary.InstancesDeleted, summary.TasksDeleted,
            summary.HistoryRowsDeleted, summary.CapturedMessagesDeleted);
        return summary;
    }

    private async Task EnsureSandboxOnAsync(CancellationToken ct)
    {
        var dto = await clockService.GetAsync(ct);
        if (!dto.SandboxOn) throw new SandboxOffException();
    }
}

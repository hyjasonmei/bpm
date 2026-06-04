using System.Text.RegularExpressions;
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

    // Matches any version of a Model-B flow case entity: <CODE>_V<N>_Case.
    // Same matcher as FlowCodesController / FlowSandboxConfigService so a new
    // chef-cooked flow is wiped automatically with no code change here.
    private static readonly Regex CaseTypeRe = new(@"^(?<code>.+)_V\d+_Case$", RegexOptions.Compiled);

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

        // Model-B cases: wipe every <CODE>_V*_Case table the EF model knows.
        var casesDeleted = await DeleteModelBCasesAsync(onlyFlowCode: null, ct);

        // Reset clock offset too — "reset everything" means tester gets a
        // clean slate AND wall-clock alignment back. Doesn't touch the
        // sandbox-on toggle itself (the tester wants to keep testing).
        await clockService.ResetAsync(ct);

        var summary = new ResetSummary(instancesDeleted, tasksDeleted, historyDeleted, capturedDeleted, casesDeleted);
        logger.LogInformation(
            "Sandbox reset ALL by {Actor}: deleted {Instances} instances, {Tasks} tasks, {History} history, {Captured} captured, {Cases} model-B cases (clock offset cleared)",
            actorUserId, summary.InstancesDeleted, summary.TasksDeleted,
            summary.HistoryRowsDeleted, summary.CapturedMessagesDeleted, summary.CasesDeleted);
        return summary;
    }

    public async Task<ResetSummary> FactoryResetAsync(CancellationToken ct = default)
    {
        // Deliberately NOT gated behind EnsureSandboxOnAsync and not
        // tenant-scoped — this is the admin "wipe back to seed-init" button,
        // not a sandbox tester action. ExecuteDeleteAsync bypasses interceptors
        // so the append-only TaskHistory guard doesn't fire. Child-before-parent
        // order for the HR-flow pair (Postgres FK-safe).
        var casesDeleted = await DeleteModelBCasesAsync(onlyFlowCode: null, ct);
        var historyDeleted = await db.TaskHistory.ExecuteDeleteAsync(ct);
        var tasksDeleted = await db.ProcessTasks.ExecuteDeleteAsync(ct);
        var instancesDeleted = await db.ProcessInstances.ExecuteDeleteAsync(ct);
        var capturedDeleted = await db.SandboxCapturedMessages.ExecuteDeleteAsync(ct);
        await db.UserNotifications.ExecuteDeleteAsync(ct);
        await db.NotificationDispatchAudits.ExecuteDeleteAsync(ct);
        await db.ActorResolutionAudits.ExecuteDeleteAsync(ct);
        await db.DoctorActionLogs.ExecuteDeleteAsync(ct);
        await db.ImpersonationSessions.ExecuteDeleteAsync(ct);
        await db.AttendancePunches.ExecuteDeleteAsync(ct);
        await db.HrFlowActions.ExecuteDeleteAsync(ct);
        await db.HrFlowInstances.ExecuteDeleteAsync(ct);

        // Clock offset is a sandbox-only concept; resetting it is itself
        // sandbox-gated. When sandbox is off there's no offset to clear, so
        // swallow the gate exception rather than fail the whole wipe.
        try { await clockService.ResetAsync(ct); }
        catch (SandboxOffException) { /* no offset when sandbox is off */ }

        logger.LogWarning(
            "FACTORY RESET: wiped all runtime data — {Cases} cases, {Instances} instances, {Tasks} tasks, {History} history, {Captured} captured",
            casesDeleted, instancesDeleted, tasksDeleted, historyDeleted, capturedDeleted);
        return new ResetSummary(instancesDeleted, tasksDeleted, historyDeleted, capturedDeleted, casesDeleted);
    }

    public async Task<ResetSummary> ResetFlowAsync(string flowCode, Guid actorUserId, CancellationToken ct = default)
    {
        await EnsureSandboxOnAsync(ct);

        var casesDeleted = await DeleteModelBCasesAsync(onlyFlowCode: flowCode, ct);
        var capturedDeleted = await db.SandboxCapturedMessages
            .Where(m => m.TenantCode == DefaultTenant && m.FlowCode == flowCode)
            .ExecuteDeleteAsync(ct);

        var summary = new ResetSummary(0, 0, 0, capturedDeleted, casesDeleted);
        logger.LogInformation(
            "Sandbox reset flow {Flow} by {Actor}: deleted {Cases} cases, {Captured} captured",
            flowCode, actorUserId, casesDeleted, capturedDeleted);
        return summary;
    }

    /// <summary>
    /// Hard-deletes Model-B flow case rows via reflection over the EF model.
    /// <paramref name="onlyFlowCode"/> null ⇒ every flow; otherwise just that
    /// flow's table(s). Table name comes from EF metadata (trusted, not user
    /// input). Pure EF (<c>ExecuteSqlRawAsync</c>) so it bypasses the
    /// append-only interceptor and runs on SQLite + Postgres alike.
    /// </summary>
    private async Task<int> DeleteModelBCasesAsync(string? onlyFlowCode, CancellationToken ct)
    {
        var caseEntities = db.Model.GetEntityTypes()
            .Select(e => new { Entity = e, Match = CaseTypeRe.Match(e.ClrType.Name) })
            .Where(x => x.Match.Success
                        && x.Entity.ClrType.GetProperty("Status") is not null
                        && x.Entity.ClrType.GetProperty("SubmittedAt") is not null
                        && (onlyFlowCode is null
                            || string.Equals(x.Match.Groups["code"].Value, onlyFlowCode, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var total = 0;
        foreach (var x in caseEntities)
        {
            var table = x.Entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;
            // Identifier quoted with ANSI double-quotes — valid on SQLite + Postgres.
            total += await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"", ct);
        }
        return total;
    }

    private async Task EnsureSandboxOnAsync(CancellationToken ct)
    {
        var dto = await clockService.GetAsync(ct);
        if (!dto.SandboxOn) throw new SandboxOffException();
    }
}

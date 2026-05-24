using System.Text.Json;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Process.Simulator;
using Bpm.Application.Spec;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Entities.Sandbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Process.Simulator;

/// <summary>
/// Reference <see cref="IProcessSimulator"/> implementation (PR-K3 §4.2).
///
/// <para>
/// <b>Approach.</b> The simulator drives the live <see cref="IProcessRuntime"/>
/// against a chosen flow code's spec, then deletes every row it produced
/// before returning. We deliberately chose <em>delete-on-finally</em> over a
/// wrapping rollback transaction:
///   - <see cref="ProcessRuntime.SubmitTaskAsync"/> opens its own
///     <c>BeginTransactionAsync</c> inside every submit. EF Core treats
///     nested transactions on a single SQLite connection as savepoints when
///     they share a connection, but the <c>SaveChangesAsync</c> +
///     <c>tx.CommitAsync</c> combo inside the runtime would be rolled back
///     by an outer rollback only when both txns share the same connection.
///     In a multi-DbContext scenario (the simulator wires its own
///     <c>AppDbContext</c> separately from the runtime's) connections
///     differ and the outer rollback wouldn't cover the inner commit.
///   - Cleanup by <see cref="ProcessInstance.Id"/> is cheap (the dataset is
///     a single instance), explicit, and makes the contract obvious in
///     code review: even if a future runtime path sneaks in a side-effect,
///     the cleanup pass tagged by id will catch it.
/// </para>
///
/// <para>
/// <b>Notification capture.</b> The simulator flips the default tenant's
/// <c>SandboxMode</c> on for the duration of the call so the production
/// <c>SandboxCapturingNotificationDispatcher</c> writes
/// <see cref="SandboxCapturedMessage"/> rows the simulator can surface as
/// <see cref="SimulationNotification"/>s. The previous sandbox state is
/// restored in the same finally that cleans up the rows — same pattern as
/// the bundle reproducibility runner (PR-J6 §11.2).
/// </para>
///
/// <para>
/// <b>Driving the flow.</b> At each step we look up the next open task,
/// auto-submit it on the assignee's behalf (Approval → Approve, userTask →
/// no patch / no decision), and capture a <see cref="SimulationStep"/>.
/// Iteration stops when the instance leaves <c>Running</c> or no open task
/// remains, capped at <see cref="MaxStepsPerSimulation"/> for safety.
/// Per-step decisions are not yet wired in v1 — Reject / branch overrides
/// land when the simulator UI grows a step-controls panel.
/// </para>
/// </summary>
public sealed class ProcessSimulator(
    AppDbContext db,
    IProcessRuntime runtime,
    ISpecLoader specLoader,
    ILogger<ProcessSimulator> logger) : IProcessSimulator
{
    private const string DefaultTenant = "default";
    private const int MaxStepsPerSimulation = 64;

    public async Task<SimulationResult> SimulateAsync(SimulationRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (string.IsNullOrWhiteSpace(req.FlowCode))
        {
            return Failed("flowCode is required");
        }

        // 1. Load spec text. We use the inline-spec overload of
        //    StartInstanceAsync, mirroring the bundle reproducibility runner
        //    — that way the simulator works even for flow codes that aren't
        //    registered with the live spec loader (Designer drafts, etc.).
        string? specJson;
        try
        {
            specJson = await specLoader.LoadAsync(DefaultTenant, req.FlowCode, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ProcessSimulator: spec load threw for {FlowCode}", req.FlowCode);
            return Failed($"failed to load spec '{req.FlowCode}': {ex.Message}");
        }
        if (string.IsNullOrWhiteSpace(specJson))
        {
            return Failed($"flow '{req.FlowCode}' not found");
        }

        // 2. Pick initiator. Caller override wins; otherwise the lowest-id
        //    user with a manager (so submitter.manager resolves) so the
        //    LEAVE flow doesn't blow up looking up a non-existent boss.
        var initiator = req.InitiatorUserId ?? await PickDefaultInitiatorAsync(ct);
        if (initiator == Guid.Empty)
        {
            return Failed("no users available to act as initiator (seed an OrgFixture first)");
        }

        // 3. Capture sandbox state and flip on so notification dispatch
        //    captures into SandboxCapturedMessages. We always restore the
        //    prior state (and clean up rows) in the finally below.
        var (prevSandboxOn, prevOffsetSeconds, prevConfigJson) = await CaptureSandboxStateAsync(ct);
        await SetSandboxStateAsync(enabled: true, offsetSeconds: prevOffsetSeconds, configJson: prevConfigJson, ct);

        Guid? createdInstanceId = null;
        var trace = new List<SimulationStep>();

        try
        {
            // 4. Start the instance via the inline-spec overload.
            StartInstanceResult start;
            try
            {
                var startCmd = new StartInstanceCommand(req.FlowCode, req.FormData, initiator);
                start = await runtime.StartInstanceAsync(startCmd, specJson, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ProcessSimulator: StartInstance failed for {FlowCode}", req.FlowCode);
                return Failed($"start failed: {ex.Message}");
            }
            createdInstanceId = start.InstanceId;

            // Seed the trace with the synthetic start row so the UI shows a
            // begin marker even if the next step errors immediately.
            trace.Add(new SimulationStep(
                NodeId: ExtractStartNodeId(specJson) ?? "start",
                NodeKind: "startEvent",
                Outcome: "auto-advanced",
                ResolvedAssigneeUserId: initiator,
                AssigneeName: await LookupUserNameAsync(initiator, ct),
                Decision: null,
                Notes: "instance started"));

            // 5. Drive forward until no open task remains or the instance
            //    leaves Running. Each iteration auto-submits one task.
            for (var step = 0; step < MaxStepsPerSimulation; step++)
            {
                var instance = await db.ProcessInstances
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == start.InstanceId, ct);
                if (instance is null) break;
                if (instance.Status != InstanceStatus.Running)
                {
                    // Walk synthetic gateway / end / notify nodes the runtime
                    // visited between the last task and termination.
                    AppendSyntheticTailAsync(trace, instance);
                    break;
                }

                var nextTask = await db.ProcessTasks
                    .AsNoTracking()
                    .Where(t => t.ProcessInstanceId == start.InstanceId
                                && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                    .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                    .FirstOrDefaultAsync(ct);
                if (nextTask is null) break;

                var actor = nextTask.ActualAssigneeUserId ?? nextTask.OriginalAssigneeUserId ?? initiator;
                Decision? decision = nextTask.NodeKind == NodeKind.Approval ? Decision.Approve : null;
                var name = await LookupUserNameAsync(actor, ct);

                try
                {
                    await runtime.SubmitTaskAsync(
                        new SubmitTaskCommand(nextTask.Id, actor, FormDataPatch: null, Decision: decision, Comment: null), ct);
                    trace.Add(new SimulationStep(
                        NodeId: nextTask.NodeId,
                        NodeKind: NodeKindLabel(nextTask.NodeKind),
                        Outcome: "completed",
                        ResolvedAssigneeUserId: actor,
                        AssigneeName: name,
                        Decision: decision?.ToString(),
                        Notes: null));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "ProcessSimulator: submit failed at node {Node}", nextTask.NodeId);
                    trace.Add(new SimulationStep(
                        NodeId: nextTask.NodeId,
                        NodeKind: NodeKindLabel(nextTask.NodeKind),
                        Outcome: "errored",
                        ResolvedAssigneeUserId: actor,
                        AssigneeName: name,
                        Decision: decision?.ToString(),
                        Notes: ex.Message));
                    break;
                }
            }

            // 6. Final form data + status, plus any captured notifications.
            var finalInstance = await db.ProcessInstances
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == start.InstanceId, ct);
            JsonElement? finalFormData = null;
            string? finalStatus = null;
            if (finalInstance is not null)
            {
                using var formDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(finalInstance.CurrentFormDataJson) ? "{}" : finalInstance.CurrentFormDataJson);
                finalFormData = formDoc.RootElement.Clone();
                finalStatus = finalInstance.Status.ToString();
            }

            // Surface any open tasks at sim end as `spawned` rows so the UI
            // shows what the user would have to act on next.
            var openTasks = await db.ProcessTasks
                .AsNoTracking()
                .Where(t => t.ProcessInstanceId == start.InstanceId
                            && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .ToListAsync(ct);
            foreach (var t in openTasks)
            {
                if (trace.Any(s => s.NodeId == t.NodeId && s.Outcome == "completed")) continue;
                var actor = t.ActualAssigneeUserId ?? t.OriginalAssigneeUserId ?? initiator;
                trace.Add(new SimulationStep(
                    NodeId: t.NodeId,
                    NodeKind: NodeKindLabel(t.NodeKind),
                    Outcome: "spawned",
                    ResolvedAssigneeUserId: actor,
                    AssigneeName: await LookupUserNameAsync(actor, ct),
                    Decision: null,
                    Notes: "open at simulation end"));
            }

            var notifications = await CaptureNotificationsAsync(start.InstanceId, ct);

            return new SimulationResult(
                Success: true,
                Error: null,
                Trace: trace,
                Notifications: notifications,
                FinalFormData: finalFormData,
                FinalStatus: finalStatus);
        }
        finally
        {
            // ALWAYS clean up — even if the simulation threw inside the try.
            try
            {
                await CleanupAsync(createdInstanceId, ct);
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx, "ProcessSimulator: cleanup failed for instance {Instance}", createdInstanceId);
            }

            try
            {
                await SetSandboxStateAsync(prevSandboxOn, prevOffsetSeconds, prevConfigJson, CancellationToken.None);
            }
            catch (Exception sbEx)
            {
                logger.LogError(sbEx, "ProcessSimulator: sandbox state restore failed");
            }
        }
    }

    private static SimulationResult Failed(string error) => new(
        Success: false,
        Error: error,
        Trace: Array.Empty<SimulationStep>(),
        Notifications: Array.Empty<SimulationNotification>(),
        FinalFormData: null,
        FinalStatus: null);

    private static string NodeKindLabel(NodeKind k) => k switch
    {
        NodeKind.UserTask => "userTask",
        NodeKind.Approval => "approval",
        NodeKind.ServiceTask => "serviceTask",
        _ => k.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Walk the spec snapshot's history events to surface gateway / notify /
    /// end node visits — the runtime writes <c>GatewayEvaluated</c> and
    /// <c>InstanceCompleted</c> rows we can mine. Lets the trace include the
    /// "between-task" nodes the runtime auto-advanced through.
    /// </summary>
    private void AppendSyntheticTailAsync(List<SimulationStep> trace, ProcessInstance instance)
    {
        // Append a synthetic terminal row when the instance has terminated
        // so the UI shows a clear "completed/cancelled/errored" marker.
        var outcome = instance.Status switch
        {
            InstanceStatus.Completed => "auto-advanced",
            InstanceStatus.Cancelled => "errored",
            InstanceStatus.Errored => "errored",
            _ => "auto-advanced",
        };
        trace.Add(new SimulationStep(
            NodeId: instance.Status.ToString().ToLowerInvariant(),
            NodeKind: "endEvent",
            Outcome: outcome,
            ResolvedAssigneeUserId: null,
            AssigneeName: null,
            Decision: null,
            Notes: instance.LastError));
    }

    private static string? ExtractStartNodeId(string specJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(specJson);
            if (!doc.RootElement.TryGetProperty("flow", out var flow)) return null;
            if (!flow.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return null;
            foreach (var n in nodes.EnumerateArray())
            {
                if (n.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    && t.GetString() == "startEvent"
                    && n.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    return id.GetString();
                }
            }
        }
        catch (JsonException) { }
        return null;
    }

    private async Task<Guid> PickDefaultInitiatorAsync(CancellationToken ct)
    {
        // Prefer a user with an explicit manager edge so spec.manager
        // resolution has a chance; fall back to any active user.
        var withMgr = await (
            from u in db.SharedPrincipals.AsNoTracking()
            where u.Type == SharedIdentity.SharedPrincipalType.User && u.Active && u.DeletedAt == null
            join m in db.SharedUserManagers.AsNoTracking() on u.Id equals m.UserId
            orderby u.Id
            select u.Id).FirstOrDefaultAsync(ct);
        if (withMgr != Guid.Empty) return withMgr;
        return await db.SharedPrincipals.AsNoTracking()
            .Where(u => u.Type == SharedIdentity.SharedPrincipalType.User && u.Active && u.DeletedAt == null)
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string?> LookupUserNameAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;
        return await db.SharedPrincipals.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<IReadOnlyList<SimulationNotification>> CaptureNotificationsAsync(Guid instanceId, CancellationToken ct)
    {
        var rows = await db.SandboxCapturedMessages.AsNoTracking()
            .Where(m => m.ProcessInstanceId == instanceId && m.Channel == SandboxChannel.Email)
            .OrderBy(m => m.CapturedAt)
            .ToListAsync(ct);

        var triggerByNotificationId = await BuildTriggerLookupAsync(instanceId, ct);

        var result = new List<SimulationNotification>(rows.Count);
        foreach (var r in rows)
        {
            var recipients = DeserializeRecipients(r.IntendedRecipientsJson);
            var trigger = (r.OriginatingNotificationId is { } id && triggerByNotificationId.TryGetValue(id, out var t))
                ? t : "";
            result.Add(new SimulationNotification(
                NotificationId: r.OriginatingNotificationId ?? "",
                Trigger: trigger,
                Subject: r.Subject ?? "",
                Recipients: recipients));
        }
        return result;
    }

    /// <summary>
    /// Pull the <c>{ id → trigger }</c> map out of the spec snapshot the
    /// runtime stored on the instance. Saves a second spec parse — the
    /// snapshot is the source of truth for what the runtime saw.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildTriggerLookupAsync(Guid instanceId, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var snapshotJson = await db.ProcessInstances.AsNoTracking()
            .Where(i => i.Id == instanceId)
            .Select(i => i.SpecSnapshotJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(snapshotJson)) return map;
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (!doc.RootElement.TryGetProperty("notifications", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return map;
            }
            foreach (var n in arr.EnumerateArray())
            {
                if (n.ValueKind != JsonValueKind.Object) continue;
                var id = n.TryGetProperty("id", out var ide) && ide.ValueKind == JsonValueKind.String ? ide.GetString() : null;
                var trigger = n.TryGetProperty("trigger", out var te) && te.ValueKind == JsonValueKind.String ? te.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(trigger))
                {
                    map[id!] = trigger!;
                }
            }
        }
        catch (JsonException) { }
        return map;
    }

    private static IReadOnlyList<string> DeserializeRecipients(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Delete every row the simulation produced. We target by id rather than
    /// by tenant so a parallel real run on the same tenant is untouched. The
    /// AuditSaveChangesInterceptor's append-only guard on TaskHistory is
    /// bypassed by ExecuteDelete (the same trick BundleRuntimeLoader uses for
    /// its scratch-tenant cleanup).
    /// </summary>
    private async Task CleanupAsync(Guid? instanceId, CancellationToken ct)
    {
        if (instanceId is not { } id) return;
        await db.SandboxCapturedMessages.Where(m => m.ProcessInstanceId == id).ExecuteDeleteAsync(ct);
        await db.TaskHistory.Where(h => h.ProcessInstanceId == id).ExecuteDeleteAsync(ct);
        await db.ProcessTasks.Where(t => t.ProcessInstanceId == id).ExecuteDeleteAsync(ct);
        await db.ProcessInstances.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
    }

    private async Task<(bool SandboxOn, long OffsetSeconds, string? ConfigJson)> CaptureSandboxStateAsync(CancellationToken ct)
    {
        var existing = await db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == DefaultTenant, ct);
        if (existing is null) return (false, 0, null);
        return (existing.SandboxMode, existing.SandboxClockOffsetSeconds, existing.SandboxConfigJson);
    }

    private async Task SetSandboxStateAsync(bool enabled, long offsetSeconds, string? configJson, CancellationToken ct)
    {
        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantCode == DefaultTenant, ct);
        if (row is null)
        {
            row = new TenantSettings { TenantCode = DefaultTenant };
            db.TenantSettings.Add(row);
        }
        row.SandboxMode = enabled;
        row.SandboxClockOffsetSeconds = offsetSeconds;
        row.SandboxConfigJson = configJson;
        await db.SaveChangesAsync(ct);
    }
}

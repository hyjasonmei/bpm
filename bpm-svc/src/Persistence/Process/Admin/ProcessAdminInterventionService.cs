using System.Text.Json;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Process.Admin;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Domain.Entities.Process;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Process.Admin;

/// <summary>
/// PR-K4 §6.1 implementation. Each method writes one TaskHistory row that
/// includes <c>actorRole = "admin"</c> in <c>PayloadJson</c>, plus whatever
/// per-action context (original/new assignee, target node, decision,
/// reason) downstream auditors need to reconstruct what happened.
///
/// <para>
/// The service composes against <see cref="IProcessRuntime"/> for ForceSubmit
/// + Terminate (so gateway routing / approval coordination / notification
/// dispatch behave identically to a regular submission), and against the
/// raw <see cref="AppDbContext"/> for ForceReassign + ForceReturn (where
/// the runtime has no equivalent state transition).
/// </para>
/// </summary>
public sealed class ProcessAdminInterventionService(
    AppDbContext db,
    IClock clock,
    IProcessRuntime runtime) : IProcessAdminInterventionService
{
    private const string DefaultTenant = "default";

    public async Task ForceReassignAsync(
        Guid taskId, Guid newAssigneeUserId, string reason, Guid actorUserId, CancellationToken ct = default)
    {
        ValidateReason(reason);

        var task = await db.ProcessTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("ProcessTask", taskId);

        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress))
            throw new ConflictException($"task {task.Id} status is {task.Status}, cannot force-reassign");

        var original = task.ActualAssigneeUserId;
        task.ActualAssigneeUserId = newAssigneeUserId;
        task.ClaimedAt = null;
        task.Status = TaskStatus.Pending;

        // Bump LastActivityAt on the parent instance so the LiveCases monitor
        // surfaces the reassignment as a fresh event.
        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == task.ProcessInstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", task.ProcessInstanceId);
        instance.LastActivityAt = clock.UtcNow;

        // We re-use HistoryEventType.TaskClaimed as the event type because
        // the resulting state shape (assignee+ClaimedAt update) is the same
        // semantic family. The actorRole + originalAssigneeUserId payload
        // marks it as an admin reassignment vs a regular claim.
        var payload = JsonSerializer.Serialize(new
        {
            actorRole = "admin",
            originalAssigneeUserId = original,
            newAssigneeUserId,
            reason,
        });
        WriteHistory(task.ProcessInstanceId, task.Id, HistoryEventType.TaskClaimed, actorUserId, payload);

        await db.SaveChangesAsync(ct);
    }

    public async Task ForceReturnAsync(
        Guid taskId, string targetNodeId, string reason, Guid actorUserId, CancellationToken ct = default)
    {
        ValidateReason(reason);
        if (string.IsNullOrWhiteSpace(targetNodeId))
            throw NewValidation(nameof(targetNodeId), "targetNodeId is required");

        var task = await db.ProcessTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("ProcessTask", taskId);
        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress))
            throw new ConflictException($"task {task.Id} status is {task.Status}, cannot force-return");

        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == task.ProcessInstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", task.ProcessInstanceId);

        using var snapshot = new SpecSnapshot(instance.SpecSnapshotJson);
        var node = snapshot.GetNode(targetNodeId)
            ?? throw new ConflictException($"node '{targetNodeId}' not found in spec snapshot");

        var now = clock.UtcNow;

        var fromNode = task.NodeId;
        task.Status = TaskStatus.Cancelled;
        task.CompletedAt = now;
        task.Decision = Decision.Return;
        task.Comment = reason;

        // Spawn a fresh placeholder task at the target node — assigned to the
        // admin so it's immediately actionable. The next regular submit (or
        // a Force Reassign) can hand it back to the right person; v1 keeps
        // this minimal because the alternative (re-running the actor
        // resolver on a return-target node mid-flow) requires plumbing the
        // resolver into this service and exercising every actor-shape
        // permutation. The history payload makes the synthetic origin
        // explicit so it isn't confused with a normal spawn.
        var spawned = new ProcessTask
        {
            Id = Guid.NewGuid(),
            TenantCode = instance.TenantCode,
            ProcessInstanceId = instance.Id,
            NodeId = node.Id,
            NodeKind = node.Type switch
            {
                "userTask" => NodeKind.UserTask,
                "approval" => NodeKind.Approval,
                "serviceTask" => NodeKind.ServiceTask,
                _ => NodeKind.UserTask,
            },
            OriginalAssigneeUserId = actorUserId,
            ActualAssigneeUserId = actorUserId,
            CandidateSetJson = JsonSerializer.Serialize(new[] { actorUserId }),
            Status = TaskStatus.Pending,
        };
        db.ProcessTasks.Add(spawned);

        instance.LastActivityAt = now;

        var returnPayload = JsonSerializer.Serialize(new
        {
            actorRole = "admin",
            fromNodeId = fromNode,
            toNodeId = targetNodeId,
            reason,
        });
        WriteHistory(instance.Id, task.Id, HistoryEventType.TaskReturned, actorUserId, returnPayload);

        var spawnPayload = JsonSerializer.Serialize(new
        {
            actorRole = "admin",
            nodeId = node.Id,
            spawnedByForceReturn = true,
            assignee = actorUserId,
        });
        WriteHistory(instance.Id, spawned.Id, HistoryEventType.TaskSpawned, actorUserId, spawnPayload);

        await db.SaveChangesAsync(ct);
    }

    public async Task ForceSubmitAsync(
        Guid taskId, JsonElement? formDataPatch, Decision? decision, string reason, Guid actorUserId,
        CancellationToken ct = default)
    {
        ValidateReason(reason);

        // Ensure the task exists up-front so the audit row precedes the
        // runtime write inside the same SaveChanges. We append the admin
        // marker history entry first, then call into the runtime — both
        // land via the same DbContext, sharing the runtime's transaction
        // boundary.
        var task = await db.ProcessTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("ProcessTask", taskId);

        // Pre-write the admin marker. This row is independent of the
        // SubmitTaskSubmitted row the runtime writes — it explicitly
        // records the override + reason, which the regular runtime row
        // does NOT carry (its payload is patch/decision/comment).
        var markerPayload = JsonSerializer.Serialize(new
        {
            actorRole = "admin",
            decision = decision?.ToString(),
            reason,
        });
        WriteHistory(task.ProcessInstanceId, task.Id, HistoryEventType.TaskSubmitted, actorUserId, markerPayload);

        await db.SaveChangesAsync(ct);

        await runtime.SubmitTaskAsAdminAsync(
            new SubmitTaskCommand(taskId, actorUserId, formDataPatch, decision, Comment: reason),
            ct);
    }

    public async Task TerminateAsync(
        Guid instanceId, string reason, Guid actorUserId, CancellationToken ct = default)
    {
        ValidateReason(reason);

        // Append the admin marker to the same DbContext, then delegate.
        // The runtime's CancelInstanceAsAdmin writes its own
        // InstanceCancelled row with payload {reason, cancelledTasks}; the
        // marker we add here makes the actorRole=admin override visible in
        // a single, isolated row that auditors can grep for.
        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", instanceId);

        var markerPayload = JsonSerializer.Serialize(new
        {
            actorRole = "admin",
            reason,
        });
        WriteHistory(instance.Id, null, HistoryEventType.InstanceCancelled, actorUserId, markerPayload);

        await db.SaveChangesAsync(ct);

        await runtime.CancelInstanceAsAdminAsync(
            new CancelInstanceCommand(instanceId, actorUserId, reason),
            ct);
    }

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw NewValidation(nameof(reason), "reason is required");
    }

    private static ValidationException NewValidation(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });

    private void WriteHistory(Guid instanceId, Guid? taskId, HistoryEventType eventType, Guid? actor, string payloadJson)
    {
        db.TaskHistory.Add(new TaskHistory
        {
            Id = Guid.NewGuid(),
            TenantCode = DefaultTenant,
            ProcessInstanceId = instanceId,
            TaskId = taskId,
            EventType = eventType,
            ActorUserId = actor,
            PayloadJson = payloadJson,
        });
    }
}

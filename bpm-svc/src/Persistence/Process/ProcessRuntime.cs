using System.Text.Json;
using System.Text.Json.Nodes;
using Bpm.Application.Common.Abstractions;
using Bpm.Application.Common.Exceptions;
using Bpm.Application.Delegation;
using Bpm.Application.Notifications;
using Bpm.Application.Process.Runtime;
using Bpm.Application.Process.Runtime.Commands;
using Bpm.Application.Spec;
using Bpm.Application.Spec.Expressions;
using Bpm.Domain.Entities.Process;
using Bpm.Domain.Spec;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskStatus = Bpm.Domain.Entities.Process.TaskStatus;

namespace Bpm.Persistence.Process;

/// <summary>
/// State-transition engine. See design.md §1, §3, §5 for invariants.
/// </summary>
public sealed class ProcessRuntime(
    AppDbContext db,
    IClock clock,
    ISpecLoader specLoader,
    IActorResolver actorResolver,
    IDelegationService delegationService,
    INotificationDispatcher notificationDispatcher,
    IExpressionEvaluator expressionEvaluator,
    ILogger<ProcessRuntime> logger) : IProcessRuntime
{
    private const string DefaultTenant = "default";

    public async Task<StartInstanceResult> StartInstanceAsync(StartInstanceCommand cmd, CancellationToken ct = default)
    {
        var json = await specLoader.LoadAsync(DefaultTenant, cmd.SpecCode, ct)
            ?? throw new NotFoundException("Spec", cmd.SpecCode);

        using var snapshot = new SpecSnapshot(json);
        var now = clock.UtcNow;

        var formDataJson = cmd.FormData.ValueKind == JsonValueKind.Undefined
            ? "{}" : cmd.FormData.GetRawText();
        if (string.IsNullOrWhiteSpace(formDataJson)) formDataJson = "{}";

        var instance = new ProcessInstance
        {
            Id = Guid.NewGuid(),
            TenantCode = string.IsNullOrWhiteSpace(snapshot.TenantCode) ? DefaultTenant : DefaultTenant,
            SpecCode = string.IsNullOrWhiteSpace(snapshot.FlowCode) ? cmd.SpecCode : snapshot.FlowCode,
            SpecVersion = snapshot.FlowVersion,
            SpecSnapshotJson = json,
            InitiatorUserId = cmd.InitiatorUserId,
            Status = InstanceStatus.Running,
            CurrentFormDataJson = formDataJson,
            StartedAt = now,
            LastActivityAt = now,
        };
        db.ProcessInstances.Add(instance);

        WriteHistory(instance.Id, null, HistoryEventType.InstanceStarted, cmd.InitiatorUserId,
            JsonSerializer.Serialize(new { specCode = instance.SpecCode, specVersion = instance.SpecVersion }));

        var startNode = snapshot.StartNode
            ?? throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure("spec", "spec has no startEvent node")
            });
        var firstOut = snapshot.GetEdgesFrom(startNode.Id).FirstOrDefault()
            ?? throw new ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure("spec", "startEvent has no outgoing edge")
            });

        var firstTaskId = await SpawnTaskAtNodeAsync(instance, snapshot, firstOut.Target, isFirstAfterStart: true, ct);

        await notificationDispatcher.DispatchAsync(snapshot, "on_submit", BuildContext(instance, snapshot, null, null), ct);

        await db.SaveChangesAsync(ct);

        return new StartInstanceResult(instance.Id, firstTaskId ?? Guid.Empty);
    }

    public async Task SubmitTaskAsync(SubmitTaskCommand cmd, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var task = await db.ProcessTasks.FirstOrDefaultAsync(t => t.Id == cmd.TaskId, ct)
            ?? throw new NotFoundException("ProcessTask", cmd.TaskId);

        if (task.ActualAssigneeUserId != cmd.ActorUserId)
            throw new ForbiddenException($"actor {cmd.ActorUserId} is not the assignee of task {task.Id}");
        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress))
            throw new ConflictException($"task {task.Id} status is {task.Status}, cannot submit");

        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == task.ProcessInstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", task.ProcessInstanceId);
        if (instance.Status != InstanceStatus.Running)
            throw new ConflictException($"instance {instance.Id} status is {instance.Status}");

        using var snapshot = new SpecSnapshot(instance.SpecSnapshotJson);
        var now = clock.UtcNow;

        var patchJson = cmd.FormDataPatch?.GetRawText();
        task.FormDataPatchJson = patchJson;
        task.Decision = cmd.Decision;
        task.Comment = cmd.Comment;
        task.Status = TaskStatus.Completed;
        task.CompletedAt = now;

        if (!string.IsNullOrEmpty(patchJson))
            instance.CurrentFormDataJson = MergeShallow(instance.CurrentFormDataJson, patchJson);
        instance.LastActivityAt = now;

        var submitPayload = JsonSerializer.Serialize(new
        {
            patch = patchJson,
            decision = cmd.Decision?.ToString(),
            comment = cmd.Comment,
        });
        WriteHistory(instance.Id, task.Id, HistoryEventType.TaskSubmitted, cmd.ActorUserId, submitPayload);

        if (task.NodeKind == NodeKind.Approval && cmd.Decision is not null)
        {
            var approvalEvt = cmd.Decision switch
            {
                Decision.Approve => HistoryEventType.ApprovalApproved,
                Decision.Reject => HistoryEventType.ApprovalRejected,
                _ => (HistoryEventType?)null,
            };
            if (approvalEvt is not null)
            {
                WriteHistory(instance.Id, task.Id, approvalEvt.Value, cmd.ActorUserId,
                    JsonSerializer.Serialize(new { decision = cmd.Decision.ToString(), comment = cmd.Comment }));

                if (cmd.Decision == Decision.Reject)
                {
                    instance.Status = InstanceStatus.Errored;
                    instance.LastError = "approval rejected";
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return;
                }
            }
        }

        // CollectionMode coordination: if siblings (same NodeId, same instance)
        // are still Pending/InProgress, do not advance yet.
        var siblingOpen = await db.ProcessTasks
            .Where(t => t.ProcessInstanceId == instance.Id && t.NodeId == task.NodeId
                        && t.Id != task.Id
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .AnyAsync(ct);
        if (siblingOpen)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        // Advance.
        var outEdges = snapshot.GetEdgesFrom(task.NodeId);
        var nextEdge = outEdges.FirstOrDefault()
            ?? throw new ConflictException($"node {task.NodeId} has no outgoing edge");
        await SpawnTaskAtNodeAsync(instance, snapshot, nextEdge.Target, isFirstAfterStart: false, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ReturnTaskAsync(ReturnTaskCommand cmd, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var task = await db.ProcessTasks.FirstOrDefaultAsync(t => t.Id == cmd.TaskId, ct)
            ?? throw new NotFoundException("ProcessTask", cmd.TaskId);
        if (task.NodeKind != NodeKind.Approval)
            throw new ConflictException($"task {task.Id} is not an Approval; return only valid for approvals");
        if (task.ActualAssigneeUserId != cmd.ActorUserId)
            throw new ForbiddenException($"actor {cmd.ActorUserId} is not the assignee of task {task.Id}");
        if (task.Status is not (TaskStatus.Pending or TaskStatus.InProgress))
            throw new ConflictException($"task {task.Id} status is {task.Status}, cannot return");

        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == task.ProcessInstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", task.ProcessInstanceId);

        using var snapshot = new SpecSnapshot(instance.SpecSnapshotJson);
        var now = clock.UtcNow;

        // Find the previous userTask: walk edges backwards from task.NodeId
        // until we land on a userTask. v1: direct predecessor only.
        var predecessor = FindPreviousUserTask(snapshot, task.NodeId)
            ?? throw new ConflictException($"no preceding userTask found for return from {task.NodeId}");

        task.Decision = Decision.Return;
        task.Comment = cmd.Comment;
        task.Status = TaskStatus.Completed;
        task.CompletedAt = now;
        instance.LastActivityAt = now;

        WriteHistory(instance.Id, task.Id, HistoryEventType.TaskReturned, cmd.ActorUserId,
            JsonSerializer.Serialize(new { comment = cmd.Comment, returnTo = predecessor }));

        await SpawnTaskAtNodeAsync(instance, snapshot, predecessor, isFirstAfterStart: false, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ClaimTaskAsync(ClaimTaskCommand cmd, CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Atomic claim: succeed only if the task is still Pending.
        var rows = await db.ProcessTasks
            .Where(t => t.Id == cmd.TaskId && t.Status == TaskStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TaskStatus.InProgress)
                .SetProperty(t => t.ClaimedAt, (DateTime?)now)
                .SetProperty(t => t.ActualAssigneeUserId, (Guid?)cmd.ActorUserId), ct);

        if (rows == 0)
            throw new ConflictException($"task {cmd.TaskId} already claimed or not pending");

        var task = await db.ProcessTasks.AsNoTracking().FirstAsync(t => t.Id == cmd.TaskId, ct);

        // Cancel sibling pool tasks for the same NodeId in the same instance.
        var siblings = await db.ProcessTasks
            .Where(t => t.ProcessInstanceId == task.ProcessInstanceId
                        && t.NodeId == task.NodeId
                        && t.Id != task.Id
                        && t.Status == TaskStatus.Pending)
            .ToListAsync(ct);

        foreach (var s in siblings)
        {
            s.Status = TaskStatus.Cancelled;
            s.CompletedAt = now;
            WriteHistory(task.ProcessInstanceId, s.Id, HistoryEventType.TaskSpawned, null,
                JsonSerializer.Serialize(new { autoCancelledOnPeerClaim = cmd.TaskId }));
        }

        WriteHistory(task.ProcessInstanceId, task.Id, HistoryEventType.TaskClaimed, cmd.ActorUserId,
            JsonSerializer.Serialize(new { claimedAt = now, cancelledSiblings = siblings.Count }));

        await db.SaveChangesAsync(ct);
    }

    public async Task CancelInstanceAsync(CancelInstanceCommand cmd, CancellationToken ct = default)
    {
        var instance = await db.ProcessInstances.FirstOrDefaultAsync(i => i.Id == cmd.InstanceId, ct)
            ?? throw new NotFoundException("ProcessInstance", cmd.InstanceId);

        // V1 rule: only initiator may cancel. Tenant-admin override is a TODO.
        if (instance.InitiatorUserId != cmd.ActorUserId)
            throw new ForbiddenException($"actor {cmd.ActorUserId} is not the initiator of instance {instance.Id}");
        if (instance.Status != InstanceStatus.Running)
            throw new ConflictException($"instance {instance.Id} status is {instance.Status}");

        var now = clock.UtcNow;
        instance.Status = InstanceStatus.Cancelled;
        instance.CancelledAt = now;
        instance.CancelReason = cmd.Reason;
        instance.LastActivityAt = now;

        var openTasks = await db.ProcessTasks
            .Where(t => t.ProcessInstanceId == instance.Id
                        && (t.Status == TaskStatus.Pending || t.Status == TaskStatus.InProgress))
            .ToListAsync(ct);
        foreach (var t in openTasks)
        {
            t.Status = TaskStatus.Cancelled;
            t.CompletedAt = now;
        }

        WriteHistory(instance.Id, null, HistoryEventType.InstanceCancelled, cmd.ActorUserId,
            JsonSerializer.Serialize(new { reason = cmd.Reason, cancelledTasks = openTasks.Count }));

        using var snapshot = new SpecSnapshot(instance.SpecSnapshotJson);
        await notificationDispatcher.DispatchAsync(snapshot, "on_cancel",
            BuildContext(instance, snapshot, null, null), ct);

        await db.SaveChangesAsync(ct);
    }

    // ----- internal helpers -----

    /// <summary>
    /// Spawn the appropriate Task(s) at a node, recursing through gateways and
    /// terminal nodes. Returns the id of the first ProcessTask created (if any).
    /// </summary>
    private async Task<Guid?> SpawnTaskAtNodeAsync(
        ProcessInstance instance,
        SpecSnapshot snapshot,
        string nodeId,
        bool isFirstAfterStart,
        CancellationToken ct)
    {
        var node = snapshot.GetNode(nodeId)
            ?? throw new ConflictException($"node '{nodeId}' missing from spec");

        switch (node.Type)
        {
            case "endEvent":
                return await CompleteInstanceAsync(instance, snapshot, ct);

            case "gateway":
                return await EvaluateGatewayAndAdvanceAsync(instance, snapshot, node.Id, ct);

            case "notify":
                // Auto-complete after dispatch — find single outgoing edge → recurse.
                await notificationDispatcher.DispatchAsync(snapshot, "on_assign",
                    BuildContext(instance, snapshot, null, null), ct);
                var ne = snapshot.GetEdgesFrom(node.Id).FirstOrDefault()
                    ?? throw new ConflictException($"notify node '{node.Id}' has no outgoing edge");
                return await SpawnTaskAtNodeAsync(instance, snapshot, ne.Target, false, ct);

            case "userTask":
            case "approval":
            case "serviceTask":
                return await SpawnExecutableNodeAsync(instance, snapshot, node, isFirstAfterStart, ct);

            case "startEvent":
                throw new ConflictException("cannot spawn task at startEvent");

            default:
                throw new ConflictException($"unknown node type '{node.Type}' for node '{node.Id}'");
        }
    }

    private async Task<Guid?> CompleteInstanceAsync(ProcessInstance instance, SpecSnapshot snapshot, CancellationToken ct)
    {
        var now = clock.UtcNow;
        instance.Status = InstanceStatus.Completed;
        instance.CompletedAt = now;
        instance.LastActivityAt = now;
        WriteHistory(instance.Id, null, HistoryEventType.InstanceCompleted, null,
            JsonSerializer.Serialize(new { completedAt = now }));
        await notificationDispatcher.DispatchAsync(snapshot, "on_complete",
            BuildContext(instance, snapshot, null, null), ct);
        return null;
    }

    private async Task<Guid?> EvaluateGatewayAndAdvanceAsync(
        ProcessInstance instance, SpecSnapshot snapshot, string gatewayId, CancellationToken ct)
    {
        var gateway = snapshot.GetGateway(gatewayId);
        var formDict = JsonToDict(instance.CurrentFormDataJson);
        var exprCtx = new ExpressionContext(
            FormData: formDict,
            Submitter: new Dictionary<string, object?> { ["userId"] = instance.InitiatorUserId.ToString() });

        EdgeDescriptor? chosen = null;
        string? chosenCondition = null;

        if (gateway is not null && gateway.Branches.Count > 0)
        {
            // Try each branch in order; first whose condition evaluates true wins.
            // Default branch is the fallback.
            foreach (var branch in gateway.Branches)
            {
                if (string.IsNullOrEmpty(branch.Condition))
                {
                    if (branch.IsDefault) { /* held for fallback */ continue; }
                    continue;
                }
                bool result;
                try { result = expressionEvaluator.EvaluateBoolean(branch.Condition, exprCtx); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "gateway {Gateway} branch condition '{Cond}' failed to evaluate; treating as false",
                        gatewayId, branch.Condition);
                    result = false;
                }
                if (result)
                {
                    chosen = snapshot.Edges.FirstOrDefault(e => e.Id == branch.EdgeId);
                    chosenCondition = branch.Condition;
                    break;
                }
            }
            if (chosen is null)
            {
                var defaultBranch = gateway.Branches.FirstOrDefault(b => b.IsDefault);
                if (defaultBranch is not null)
                {
                    chosen = snapshot.Edges.FirstOrDefault(e => e.Id == defaultBranch.EdgeId);
                    chosenCondition = defaultBranch.Condition ?? "(default)";
                }
            }
        }
        else
        {
            // No decisions[] entry — fall back to evaluating outgoing edges' inline conditions.
            foreach (var e in snapshot.GetEdgesFrom(gatewayId))
            {
                if (string.IsNullOrEmpty(e.Condition))
                {
                    if (e.IsDefault && chosen is null) { chosen = e; chosenCondition = "(default)"; }
                    continue;
                }
                bool result;
                try { result = expressionEvaluator.EvaluateBoolean(e.Condition, exprCtx); }
                catch { result = false; }
                if (result) { chosen = e; chosenCondition = e.Condition; break; }
            }
        }

        if (chosen is null)
            throw new ConflictException($"gateway '{gatewayId}' produced no chosen edge");

        WriteHistory(instance.Id, null, HistoryEventType.GatewayEvaluated, null,
            JsonSerializer.Serialize(new { gatewayId, chosenEdgeId = chosen.Id, chosenTarget = chosen.Target, condition = chosenCondition }));

        return await SpawnTaskAtNodeAsync(instance, snapshot, chosen.Target, false, ct);
    }

    private async Task<Guid?> SpawnExecutableNodeAsync(
        ProcessInstance instance,
        SpecSnapshot snapshot,
        NodeDescriptor node,
        bool isFirstAfterStart,
        CancellationToken ct)
    {
        var nodeKind = node.Type switch
        {
            "userTask" => NodeKind.UserTask,
            "approval" => NodeKind.Approval,
            "serviceTask" => NodeKind.ServiceTask,
            _ => throw new ConflictException($"unsupported executable node type '{node.Type}'"),
        };

        ActorRef? actorRef = null;
        if (nodeKind == NodeKind.UserTask)
        {
            var ut = snapshot.GetUserTask(node.Id);
            // First userTask after StartEvent always uses initiator; later
            // userTasks resolve via permissions.submitter ("self" / "role:X").
            if (isFirstAfterStart || ut is null || string.IsNullOrEmpty(ut.Submitter)
                || ut.Submitter == "self" || ut.Submitter == "submitter")
            {
                return await SpawnSingleAssigneeTaskAsync(instance, snapshot, node, nodeKind, instance.InitiatorUserId, ct);
            }
            actorRef = ParseSimpleSubmitter(ut.Submitter);
        }
        else if (nodeKind == NodeKind.Approval)
        {
            var approval = snapshot.GetApproval(node.Id)
                ?? throw new ConflictException($"approval node '{node.Id}' missing approvals[] entry");
            if (approval.Approver is null)
                throw new ConflictException($"approval '{node.Id}' has no approver");
            actorRef = JsonSerializer.Deserialize<ActorRef>(approval.Approver.Value.GetRawText())
                ?? throw new ConflictException($"approval '{node.Id}' approver failed to deserialize");
        }
        else
        {
            // serviceTask: no actor; spawn with no assignee, status InProgress.
            return await SpawnServiceTaskAsync(instance, node, ct);
        }

        if (actorRef is null)
            throw new NotImplementedException($"could not derive actor for node '{node.Id}'");

        var resCtx = new ResolutionContext(
            SubmitterUserId: instance.InitiatorUserId,
            FormData: JsonToJsonElementDict(instance.CurrentFormDataJson),
            FlowCode: instance.SpecCode,
            StepCode: node.Id);
        var resolution = await actorResolver.ResolveAsync(actorRef, resCtx, ct);
        if (resolution is ResolutionResult.Failure failure)
            throw new ConflictException($"actor resolver failed for node '{node.Id}': {failure.Error.Kind}: {failure.Error.Reason}");
        var success = (ResolutionResult.Success)resolution;
        if (success.UserIds.Count == 0)
            throw new ConflictException($"actor resolver returned no users for node '{node.Id}'");

        var candidateJson = JsonSerializer.Serialize(success.UserIds);
        var now = clock.UtcNow;
        Guid? firstTaskId = null;
        var assignees = new HashSet<Guid>();
        var approvers = nodeKind == NodeKind.Approval ? new HashSet<Guid>() : null;

        foreach (var candidate in success.UserIds)
        {
            var delegate_ = await delegationService.GetActiveDelegateAsync(candidate, now, ct);
            var actual = delegate_ ?? candidate;
            assignees.Add(actual);
            if (approvers is not null) approvers.Add(actual);

            var task = new ProcessTask
            {
                Id = Guid.NewGuid(),
                TenantCode = instance.TenantCode,
                ProcessInstanceId = instance.Id,
                NodeId = node.Id,
                NodeKind = nodeKind,
                OriginalAssigneeUserId = candidate,
                ActualAssigneeUserId = actual,
                CandidateSetJson = candidateJson,
                Status = TaskStatus.Pending,
                DueAt = null,
            };
            db.ProcessTasks.Add(task);
            firstTaskId ??= task.Id;

            WriteHistory(instance.Id, task.Id, HistoryEventType.TaskSpawned, null,
                JsonSerializer.Serialize(new
                {
                    nodeId = node.Id,
                    nodeKind = nodeKind.ToString(),
                    candidate,
                    actualAssignee = actual,
                }));

            if (delegate_ is not null)
            {
                WriteHistory(instance.Id, task.Id, HistoryEventType.DelegationApplied, null,
                    JsonSerializer.Serialize(new { original = candidate, actual = delegate_.Value }));
            }
        }

        instance.LastActivityAt = now;
        await notificationDispatcher.DispatchAsync(snapshot, "on_assign",
            BuildContext(instance, snapshot, approvers, assignees), ct);
        return firstTaskId;
    }

    private async Task<Guid?> SpawnSingleAssigneeTaskAsync(
        ProcessInstance instance,
        SpecSnapshot snapshot,
        NodeDescriptor node,
        NodeKind nodeKind,
        Guid assigneeUserId,
        CancellationToken ct)
    {
        var now = clock.UtcNow;
        var delegate_ = await delegationService.GetActiveDelegateAsync(assigneeUserId, now, ct);
        var actual = delegate_ ?? assigneeUserId;

        var task = new ProcessTask
        {
            Id = Guid.NewGuid(),
            TenantCode = instance.TenantCode,
            ProcessInstanceId = instance.Id,
            NodeId = node.Id,
            NodeKind = nodeKind,
            OriginalAssigneeUserId = assigneeUserId,
            ActualAssigneeUserId = actual,
            CandidateSetJson = JsonSerializer.Serialize(new[] { assigneeUserId }),
            Status = TaskStatus.Pending,
        };
        db.ProcessTasks.Add(task);

        WriteHistory(instance.Id, task.Id, HistoryEventType.TaskSpawned, null,
            JsonSerializer.Serialize(new
            {
                nodeId = node.Id,
                nodeKind = nodeKind.ToString(),
                candidate = assigneeUserId,
                actualAssignee = actual,
            }));
        if (delegate_ is not null)
        {
            WriteHistory(instance.Id, task.Id, HistoryEventType.DelegationApplied, null,
                JsonSerializer.Serialize(new { original = assigneeUserId, actual = delegate_.Value }));
        }

        instance.LastActivityAt = now;
        await notificationDispatcher.DispatchAsync(snapshot, "on_assign",
            BuildContext(instance, snapshot, null, new HashSet<Guid> { actual }), ct);
        return task.Id;
    }

    private Task<Guid?> SpawnServiceTaskAsync(ProcessInstance instance, NodeDescriptor node, CancellationToken ct)
    {
        // Service tasks: spawn an InProgress task placeholder + immediate
        // completion is the eventual behaviour. v1 keeps the row in InProgress
        // for visibility; integration runner is owned by a later change.
        var task = new ProcessTask
        {
            Id = Guid.NewGuid(),
            TenantCode = instance.TenantCode,
            ProcessInstanceId = instance.Id,
            NodeId = node.Id,
            NodeKind = NodeKind.ServiceTask,
            Status = TaskStatus.InProgress,
        };
        db.ProcessTasks.Add(task);
        WriteHistory(instance.Id, task.Id, HistoryEventType.TaskSpawned, null,
            JsonSerializer.Serialize(new { nodeId = node.Id, nodeKind = "ServiceTask", placeholder = true }));
        return Task.FromResult<Guid?>(task.Id);
    }

    private static ActorRef ParseSimpleSubmitter(string submitter)
    {
        if (submitter.StartsWith("role:", StringComparison.Ordinal))
        {
            var code = submitter[5..];
            return new RoleActorRef(code);
        }
        throw new NotImplementedException($"submitter '{submitter}' not yet supported (v1 supports 'self' / 'submitter' / 'role:X')");
    }

    private static string? FindPreviousUserTask(SpecSnapshot snapshot, string nodeId)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<string>();
        queue.Enqueue(nodeId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!visited.Add(cur)) continue;
            foreach (var edge in snapshot.Edges.Where(e => e.Target == cur))
            {
                var src = snapshot.GetNode(edge.Source);
                if (src is null) continue;
                if (src.Type == "userTask") return src.Id;
                if (src.Type == "startEvent") return null;
                queue.Enqueue(src.Id);
            }
        }
        return null;
    }

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

    private static NotificationContext BuildContext(ProcessInstance instance, SpecSnapshot snapshot,
        IReadOnlySet<Guid>? approvers, IReadOnlySet<Guid>? assignees)
    {
        return new NotificationContext(
            InstanceId: instance.Id,
            SpecCode: instance.SpecCode,
            InitiatorUserId: instance.InitiatorUserId,
            FormData: JsonToDict(instance.CurrentFormDataJson),
            CurrentApprovers: approvers,
            CurrentAssignees: assignees);
    }

    /// <summary>
    /// Shallow merge: top-level keys in <paramref name="patchJson"/> overwrite
    /// keys in <paramref name="baseJson"/>. Nested objects are replaced wholesale.
    /// </summary>
    internal static string MergeShallow(string baseJson, string patchJson)
    {
        var baseNode = JsonNode.Parse(string.IsNullOrWhiteSpace(baseJson) ? "{}" : baseJson) as JsonObject
            ?? new JsonObject();
        var patchNode = JsonNode.Parse(string.IsNullOrWhiteSpace(patchJson) ? "{}" : patchJson) as JsonObject;
        if (patchNode is null) return baseNode.ToJsonString();

        foreach (var kv in patchNode.ToList())
        {
            baseNode.Remove(kv.Key);
            // detach value from old parent before re-attaching
            patchNode.Remove(kv.Key);
            baseNode[kv.Key] = kv.Value;
        }
        return baseNode.ToJsonString();
    }

    /// <summary>
    /// Convert a JSON object string into a flat dictionary suitable for CEL.
    /// Numbers become long/double, booleans bool, strings string, nulls null.
    /// Nested JsonElement values are passed through (CEL treats them as Dyn).
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> JsonToDict(string json)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = ConvertJson(prop.Value);
        }
        return dict;
    }

    private static object? ConvertJson(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            case JsonValueKind.Number:
                // Prefer integer when the JSON literal lacks a fractional/exponent part.
                // System.Text.Json's TryGetInt64 succeeds for both 5 and 5.0, so we
                // additionally inspect the raw text to keep the type closer to the
                // author's intent — CEL's numeric overload resolution is type-strict.
                var raw = el.GetRawText();
                if (!raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E')
                    && el.TryGetInt64(out var l)) return l;
                return el.GetDouble();
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return el.Clone();
            default:
                return null;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> JsonToJsonElementDict(string json)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return dict;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.Clone();
        }
        return dict;
    }
}

# Design notes

## 1. Why a single ProcessRuntime entry point instead of multiple services

We considered splitting into `IInstanceLifecycle` (start / cancel) + `ITaskActions` (claim / submit / return) + `IStateTransitioner`. Three injection points, three test surfaces.

Rejected because every state mutation chains:

- Submit a task → spawn next tasks → resolve recipients → fire notifications → write history
- One in-memory database transaction wrapping all of it
- Splitting forces either cross-service transactions (messy with EF Core) or a coordinator that effectively *is* the runtime

A single `IProcessRuntime` with focused command methods is simpler to reason about and easier to keep transactional.

## 2. Spec snapshot — copy at start, immutable thereafter

The proposal commits to deep-copying `spec.json` into `ProcessInstance.SpecSnapshotJson` at `StartInstanceAsync`. Alternative considered: store spec_id + version, look up the immutable spec row at every state transition.

Rejected because:

- We don't yet have a `ProcessVersion` table (deferred). Adding one to this change inflates scope.
- Even with a versions table, a JSON column lookup-per-transition has the same DB cost as a snapshot read; snapshot is simpler.
- Snapshots are self-contained: archiving an old instance to cold storage doesn't lose schema.
- Compliance answer is direct: "what spec did this instance run? — see `instances.{id}.spec_snapshot_json` byte-for-byte".

Cost: storage. A typical SME flow's spec is 10-30 KB. 10K instances = 100-300 MB. Acceptable for SQLite POC and Postgres prod. If/when we hit storage pressure, switch to a `ProcessVersion` table with content-addressed dedupe.

## 3. Task spawning when CandidateSet has multiple users

For approval refs that resolve to multiple users (`{ type: 'collection', mode: 'all', actors: [...] }` — all must approve; or `mode: 'any', min_approvals: 2` — any 2 of 3), the runtime needs to behave correctly:

- `collection / mode: 'all'` → spawn one Task per resolved user; instance advances when ALL are Completed.
- `collection / mode: 'any', min_approvals: N` → spawn one Task per resolved user; instance advances when N are Completed; remaining Tasks become `Cancelled` automatically.
- `functional_members` (a whole team) → conceptually `mode: 'any', min_approvals: 1` — spawn one Task per member, first to claim wins, others auto-cancelled.

The Task table holds `CandidateSetJson` (full set) for traceability; `OriginalAssigneeUserId` is the *one* user this row represents (one row per candidate). The instance progresses based on Status counts across candidate Tasks.

For approvals using `expr:submitter.manager` (single user expected), there's exactly one Task. For `functional_members:hr` (3 active users), there are 3 Tasks; first to claim sets ClaimedAt, others get Cancelled with an `AutoCancelledOnPeerClaim` history reason.

## 4. State machine — transition table

| Current node | Outgoing edge condition | Action |
|---|---|---|
| StartEvent | (always) | spawn Tasks for the next node |
| UserTask | submit | apply form patch; spawn next node |
| Approval | approve | record decision; spawn next node |
| Approval | reject | record decision; mark instance `Errored` (or follow `gateway` if spec routes rejects) |
| Approval | return | spawn a *new* Task at the previous userTask node |
| Gateway | (evaluate condition) | choose edge based on condition_expr against current form_data |
| Notify | (auto) | dispatch notification, complete Task immediately, advance |
| ServiceTask | (auto, future) | placeholder — currently logs and advances |
| EndEvent | (terminal) | mark instance `Completed`, fire on_complete |

For now, gateway condition evaluation uses the still-deferred CEL parser (separate change `add-cel-expressions`). Until CEL lands, a minimal subset (literal equality + numeric comparison + simple AND/OR) is implemented inline; documented in design §10.

## 5. Hooks — order of operations on TaskSubmit

Pseudocode:

```
SubmitTaskAsync(taskId, patch, decision, comment, actor):
  open transaction
  load Task; verify actor == task.ActualAssigneeUserId; verify Status in (Pending, InProgress)

  // Apply form patch + decision
  task.FormDataPatchJson = patch
  task.Decision = decision
  task.Comment = comment
  task.Status = Completed
  task.CompletedAt = now
  instance.CurrentFormDataJson = mergeShallow(instance.CurrentFormDataJson, patch)

  // Write history
  writeHistory(TaskSubmitted, task.Id, actor, payload={patch, decision, comment})

  // Determine next node from spec snapshot
  nextNode = resolveNextNode(spec, task.NodeId, decision)

  // Spawn next Task(s)
  if nextNode is gateway:
    chosenEdge = evaluateGateway(spec.gateways[nextNode], instance.CurrentFormDataJson)
    nextNode = chosenEdge.target
    writeHistory(GatewayEvaluated, ..., payload={chosenEdge, conditionResult})
  if nextNode is endEvent:
    instance.Status = Completed
    instance.CompletedAt = now
    writeHistory(InstanceCompleted, ...)
    dispatchNotifications(spec.notifications.where(t.trigger == on_complete), context)
  else:
    candidateSet = actorResolver.resolve(spec.userTasks[nextNode].assignee or spec.approvals[nextNode].approver, ctx)
    foreach candidate in candidateSet:
      delegate = delegationService.GetActiveDelegate(candidate, now)
      actualAssignee = delegate?.DelegateUserId ?? candidate
      newTask = Task { OriginalAssigneeUserId=candidate, ActualAssigneeUserId=actualAssignee, ... }
      insert newTask
      writeHistory(TaskSpawned, newTask.Id, system, payload={candidate, ...})
      if delegate is not null:
        writeHistory(DelegationApplied, newTask.Id, system, payload={original=candidate, actual=actualAssignee, delegationId=delegate.Id})
    dispatchNotifications(spec.notifications.where(t.trigger == on_assign), context)

  commit transaction
```

Notification dispatch happens within the same transaction. Channel adapters (Email / In-App) are *not* called synchronously here; the dispatcher inserts `NotificationDelivery` rows with `Status = Queued` and the worker picks them up asynchronously. Same DB, eventually consistent.

## 6. Why the state machine is in code, not data

We considered modeling the state machine as a separate table (`StateTransition` rules per node). Rejected — the BPMN node types are a small fixed set (start/end/userTask/approval/gateway/notify/serviceTask). Each has well-known semantics. A code switch is faster to read, easier to test, and hard to corrupt with bad data.

If we ever need customer-defined node types (probably never), then revisit. For now: flat switch in `ProcessRuntime.cs`.

## 7. TaskHistory append-only enforcement

`AuditSaveChangesInterceptor` already exists. We extend it (or add a sibling `TaskHistoryAppendOnlyInterceptor`) to inspect the change tracker and reject any UPDATE or DELETE on TaskHistory entities. SQL-level: a check via SQLite trigger or `INSTEAD OF UPDATE/DELETE` rule could also work, but EF interceptor is sufficient for our app-layer guarantee.

This is a hard requirement for ISO 9001 / IATF 16949 compliance — the partner's customers are TS 16949 auditees. Documented in spec scenarios (see bpm-process-runtime/spec.md).

Test: an integration test loads a TaskHistory row, modifies a field, calls `SaveChanges` — expects an exception.

## 8. Form data merge semantics

`mergeShallow(base, patch)` — top-level keys in `patch` overwrite those in `base`. Nested objects: replaced wholesale (no deep merge). Repeater field arrays: replaced wholesale.

Rationale: deep merge introduces ambiguity ("does an empty patch field clear the original?") that confuses users. Shallow + wholesale is predictable: if a userTask wants to keep an old field unchanged, it doesn't include it in the patch.

For repeater fields specifically (line items): if step 1 captured 3 items and step 2's form doesn't include the field, it stays 3 items. If step 2's form DOES include the same repeater field, that step's data replaces it entirely.

## 9. Auth / permission model on Task actions

Per ProcessRuntime requirement spec, task action permissions:

- `submit` / `claim` / `return` — only by `ActualAssigneeUserId`
- `read` (the GET task endpoint) — by `ActualAssigneeUserId`, `InitiatorUserId` (read-only), `tenant_admin`, `flow_admin:<spec_code>`
- `cancel` instance — by `InitiatorUserId` or `tenant_admin`

Enforced at controller level via authorization handlers; the runtime service throws `ForbiddenException` if its caller bypasses the controller (defense in depth).

## 10. Minimal expression language for gateways (until CEL)

Until `add-cel-expressions` lands, the runtime needs to evaluate `gateway.condition_expr` somehow. Implementation: a tiny parser supporting:

- Literal equality: `field == "value"`, `field != "value"`
- Numeric comparison: `amount >= 50000`, `days < 7`
- Logical AND / OR: `amount >= 50000 && category == "it"`
- Field path: dotted access like `leave.days` reading from `instance.CurrentFormDataJson`

Anything more complex (string functions, list operations, math) is rejected with `ValidationFailed("expression not supported until CEL change lands")`. Every existing sample spec's gateway expressions fit within this minimal subset.

When CEL lands, swap the implementation behind `IExpressionEvaluator`; no migration of stored data needed (expressions live in spec snapshot text, parsed at evaluation time).

## 11. Concurrency considerations

Multiple users could act on different tasks of the same instance simultaneously (e.g., a `mode: 'all'` collection of approvers). Each `SubmitTaskAsync` opens its own transaction and updates only its Task row + checks instance state. Conflicting updates (two users submitting at exactly the same moment) are serialized by SQLite's per-database write lock (or Postgres row-level locks).

Edge case: two users claim the same pool task in the same millisecond. Solution: claim is `UPDATE Tasks SET ClaimedAt = ?, Status = InProgress WHERE Id = ? AND Status = Pending`. Whoever wins the WHERE clause gets it; the other receives a "already claimed" error.

## 12. Open questions

- **Reject path semantics**: does an approval rejection always end the instance, or can the spec route rejects to a specific node? Today: spec doesn't define reject-routing. We treat reject as `instance.Status = Errored` unless a future spec extension adds `reject_to` edges. Document this default.
- **Return path depth**: when an approval returns, do we go back to the *previous* userTask, or to a specific named node? Today: go to previous. If multi-step return is needed, future extension adds `return_to: nodeId`.
- **Idempotency on retry**: HTTP clients sometimes retry. SubmitTask currently is not idempotent — retrying a successful submit would error (Task already Completed). Acceptable for v1; could add idempotency key header later.
- **Big SpecSnapshotJson**: at 10s of MB, the Task spawn copy/parse cost matters. Mitigation: cache parsed spec per instance in a request-scoped service; recreate only on instance load. Defer optimization until profiled.

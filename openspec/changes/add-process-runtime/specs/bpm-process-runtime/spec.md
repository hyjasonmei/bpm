## ADDED Requirements

### Requirement: ProcessInstance carries the full spec snapshot at start

The system SHALL persist a `ProcessInstance` entity per running case. At `StartInstanceAsync` time, the runtime MUST deep-copy the active spec.json into `ProcessInstance.SpecSnapshotJson`. All subsequent runtime decisions for this instance — task spawning, gateway evaluation, recipient resolution, notification dispatch — MUST read from the snapshot, not from any live spec source. Editing a spec after instance start MUST NOT affect any in-flight instance.

#### Scenario: Snapshot captures spec at start

- **GIVEN** the LEAVE spec at version 3 has notification N1 with subject "abc"
- **WHEN** Wilson starts a LEAVE instance
- **THEN** `instance.SpecSnapshotJson` contains the v3 spec verbatim, including N1's "abc" subject

#### Scenario: Spec edits do not affect running instance

- **GIVEN** Wilson's instance is mid-flight with v3 snapshot
- **WHEN** an admin edits the spec to version 4 changing N1's subject to "xyz"
- **AND** Wilson's instance reaches the trigger that fires N1
- **THEN** the dispatched notification uses subject "abc" (from snapshot) — NOT "xyz"

### Requirement: Task represents one assignment with original and actual assignees

The system SHALL persist a `ProcessTask` (named to avoid System.Threading.Task collision) entity for every UserTask, Approval, and Notify node spawned during instance execution. Each Task SHALL carry both `OriginalAssigneeUserId` (resolver output) and `ActualAssigneeUserId` (post-delegation transform). When delegation is applied, BOTH fields MUST be persisted, and a `DelegationApplied` `TaskHistory` row MUST be written in the same transaction.

#### Scenario: Task with no delegation

- **GIVEN** Yang has no active delegation
- **WHEN** the runtime spawns a Task for Yang as approver
- **THEN** `task.OriginalAssigneeUserId = Yang`, `task.ActualAssigneeUserId = Yang`; no DelegationApplied event

#### Scenario: Task with active delegation transforms

- **GIVEN** Yang has active delegation pointing at Lin
- **WHEN** the runtime spawns a Task originally for Yang
- **THEN** `task.OriginalAssigneeUserId = Yang`, `task.ActualAssigneeUserId = Lin`
- **AND** a `DelegationApplied` history row is written with payload `{ original: Yang, actual: Lin, delegationId: ... }`

#### Scenario: Inactive delegate falls back

- **GIVEN** Yang has active delegation to Lin, but Lin's IsActive = false
- **WHEN** the runtime spawns a Task for Yang
- **THEN** `task.ActualAssigneeUserId = Yang` (fallback to original) with a fallback reason in audit

### Requirement: TaskHistory is append-only

The system SHALL block UPDATE and DELETE on `TaskHistory` rows at the application layer. Attempting to modify or delete a TaskHistory entity through EF Core MUST throw an exception at SaveChanges time. This is a compliance hard requirement (ISO 9001 / IATF 16949). The append-only constraint is enforced via an EF SaveChanges interceptor inspecting the change tracker.

#### Scenario: Modifying TaskHistory throws

- **GIVEN** a TaskHistory row exists with `EventType = TaskSubmitted`
- **WHEN** code loads the row, mutates a field, and calls SaveChanges
- **THEN** SaveChanges throws an InvalidOperationException with a clear "TaskHistory is append-only" message

#### Scenario: Deleting TaskHistory throws

- **WHEN** code attempts `dbContext.TaskHistories.Remove(row); SaveChanges()`
- **THEN** SaveChanges throws

### Requirement: TaskHistory captures every state-changing event

The runtime SHALL write at least one `TaskHistory` row per state mutation. Required event types: `InstanceStarted`, `TaskSpawned`, `TaskClaimed`, `TaskSubmitted`, `TaskReturned`, `ApprovalApproved`, `ApprovalRejected`, `NotificationDispatched`, `GatewayEvaluated`, `InstanceCompleted`, `InstanceCancelled`, `DelegationApplied`. Each row carries `PayloadJson` with event-specific data sufficient for replay or forensic analysis.

#### Scenario: Submit task writes TaskSubmitted

- **WHEN** Wilson submits task_apply with form patch
- **THEN** a TaskHistory row is written with EventType = TaskSubmitted, ActorUserId = Wilson, PayloadJson containing the form patch

#### Scenario: Gateway evaluation writes GatewayEvaluated

- **WHEN** the runtime evaluates `gateway_days` against `days = 8`, choosing the `>= 7` edge
- **THEN** a TaskHistory row is written with EventType = GatewayEvaluated, payload `{ chosenEdge, conditionResult: true, conditionExpr, formDataAtEvaluation }`

#### Scenario: Sequence reconstructable from history

- **GIVEN** a completed LEAVE instance for Wilson
- **WHEN** an admin queries the instance's TaskHistory ordered by CreatedAt
- **THEN** the events form a complete narrative: InstanceStarted → TaskSpawned (apply) → TaskSubmitted (apply) → TaskSpawned (manager_approve) → TaskClaimed → ApprovalApproved → TaskSpawned (hr_archive) → TaskSubmitted → InstanceCompleted

### Requirement: ProcessRuntime spawns Tasks per resolver candidate

For UserTask and Approval nodes, the runtime SHALL invoke `IActorResolver.Resolve(spec.<node>.assignee or .approver, ctx)` and spawn one `ProcessTask` row per resolved candidate. The candidate set is recorded in `Task.CandidateSetJson`. Behavior depends on the resolver result shape:

- Single user → one Task; instance progresses when that Task completes
- `collection mode='all'` → one Task per candidate; all must complete; instance progresses when last completes
- `collection mode='any', min_approvals=N` → one Task per candidate; instance progresses when N complete; remaining auto-Cancelled
- `functional_members` (whole team) → one Task per member; first to claim wins; others auto-Cancelled

#### Scenario: Single user single Task

- **WHEN** spec has approval with `approver = expr:submitter.manager` and Wilson's manager is Yang
- **THEN** one Task is spawned with OriginalAssigneeUserId = Yang

#### Scenario: functional_members spawns one per member

- **GIVEN** Department `財務部` (function_tag = finance) has 3 active users (u_a, u_b, u_c)
- **WHEN** spec has userTask with `assignee = functional_members:finance`
- **THEN** 3 Tasks are spawned, OriginalAssigneeUserId = u_a, u_b, u_c respectively
- **AND** when u_a claims, u_b's and u_c's Tasks are auto-Cancelled with reason `AutoCancelledOnPeerClaim`

#### Scenario: Collection mode=all requires all completions

- **GIVEN** spec has approval `{ type: 'collection', mode: 'all', actors: [VP, CFO] }`
- **WHEN** the runtime spawns Tasks for VP and CFO
- **AND** VP submits Approve but CFO has not yet submitted
- **THEN** the instance does NOT advance; remains at the collection node

### Requirement: Submit task validates actor permission

`SubmitTaskAsync` SHALL reject when the calling user is not the Task's `ActualAssigneeUserId`. The error MUST be `ForbiddenException` returning HTTP 403. The runtime MUST NOT silently accept the submission as if the assignee did it.

#### Scenario: Wrong user submits

- **GIVEN** Task T1 has ActualAssigneeUserId = Yang (post-delegation)
- **WHEN** Wilson tries to submit T1
- **THEN** the call throws ForbiddenException; the Task remains Pending; no history event is written

#### Scenario: Right user submits

- **WHEN** Yang submits T1 with form patch and decision = Approve
- **THEN** Task is Completed; history row written; next node spawned

### Requirement: Gateway evaluates condition_expr against current form data

When the runtime advances past a Gateway node, it SHALL evaluate each outgoing edge's `condition_expr` (a string expression) against the current `ProcessInstance.CurrentFormDataJson`. The first edge whose condition evaluates to `true` is taken. If no condition matches and a `default` edge exists, that edge is taken. If neither matches, the instance transitions to `Errored` status with a `GatewayEvaluated` history row recording the failed evaluation.

#### Scenario: Condition matches

- **GIVEN** gateway has edges `[{ condition: "days >= 7", target: VP }, { isDefault: true, target: HR }]`
- **AND** instance.CurrentFormDataJson = `{ "days": 8 }`
- **WHEN** runtime evaluates the gateway
- **THEN** the chosen edge points to VP; GatewayEvaluated history row carries `{ chosenEdge, conditionResult: true }`

#### Scenario: Default edge taken

- **GIVEN** the same gateway with `days = 5`
- **WHEN** runtime evaluates
- **THEN** the default edge to HR is taken

#### Scenario: No matching edge errors instance

- **GIVEN** a gateway with no default and conditions that all evaluate false
- **WHEN** runtime evaluates
- **THEN** instance.Status = Errored; LastError set; GatewayEvaluated history row carries `{ allBranchesFailed: true }`

### Requirement: Form data accumulates via shallow merge

`ProcessInstance.CurrentFormDataJson` SHALL be the merged accumulator of all submitted `Task.FormDataPatchJson` values. Merge is shallow at the top level: a key in the patch overwrites the same key in base. Nested objects are replaced wholesale. Repeater field arrays are replaced wholesale. The runtime MUST persist the merged state on every TaskSubmitted event.

#### Scenario: New field added

- **GIVEN** instance has form data `{ leave_type: '特休' }`
- **WHEN** Yang's approval submits patch `{ approver_comment: 'OK' }`
- **THEN** instance.CurrentFormDataJson = `{ leave_type: '特休', approver_comment: 'OK' }`

#### Scenario: Existing field replaced

- **GIVEN** instance has `{ amount: 50000 }`
- **WHEN** a userTask submits patch `{ amount: 60000 }`
- **THEN** instance.CurrentFormDataJson.amount = 60000

#### Scenario: Repeater field replaced wholesale

- **GIVEN** instance has `{ items: [{ name: 'A' }, { name: 'B' }] }`
- **WHEN** a userTask submits patch `{ items: [{ name: 'C' }] }`
- **THEN** instance.CurrentFormDataJson.items = `[{ name: 'C' }]` (B and A discarded)

### Requirement: Notification hooks fire at trigger events

The runtime SHALL invoke `INotificationDispatcher.DispatchAsync` for every notification spec whose `trigger` matches the current state event. Trigger mapping:

- `on_submit` — when `StartInstanceAsync` completes successfully
- `on_assign` — when each new Task is spawned (one dispatch per Task)
- `on_approve` — when Approval Task submits Decision = Approve
- `on_reject` — when Approval Task submits Decision = Reject
- `on_complete` — when instance.Status transitions to Completed

The dispatcher receives a fresh `NotificationContext` populated with the current instance + task state (initiator, current_approver = the just-actioned Task's actualAssignee, current_assignee, form_data). Dispatch happens within the same DB transaction as the state mutation; channel adapters run asynchronously via the worker.

#### Scenario: on_assign fires per task spawn

- **GIVEN** spec has notification N1 with trigger = `on_assign`, recipients = `[current_approver]`
- **WHEN** runtime spawns a Task for Yang
- **THEN** INotificationDispatcher.DispatchAsync is called once with a context where CurrentApproverUserId = Yang's actual assignee (post-delegation)

#### Scenario: on_complete fires once

- **WHEN** instance reaches end_event
- **THEN** dispatcher is called exactly once for each notification with trigger = `on_complete`

### Requirement: Cancel instance cascades to open tasks

`CancelInstanceAsync` SHALL set instance.Status = Cancelled, CancelledAt, CancelReason, and bulk-update all associated Tasks with `Status IN (Pending, InProgress)` to `Cancelled`. One TaskHistory row MUST be written for the instance cancellation; one row per cancelled Task.

#### Scenario: Cancel mid-flight

- **GIVEN** an instance with 1 InProgress Task and 2 Pending Tasks
- **WHEN** the initiator calls cancel
- **THEN** instance.Status = Cancelled; all 3 Tasks Status = Cancelled; 4 history rows total (1 InstanceCancelled + 3 task auto-cancelled)

#### Scenario: Only initiator or admin can cancel

- **WHEN** a non-initiator non-admin user calls cancel
- **THEN** ForbiddenException

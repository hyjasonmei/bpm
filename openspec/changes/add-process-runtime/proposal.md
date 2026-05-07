## Why

Five proposals deep, the system can describe a flow precisely (BPMN nodes, ActorRef, FormField, repeater, notifications, delegation, viewers, etc.) — but it cannot *run* one. There is no concept of "an instance of the LEAVE flow that Wilson submitted on 2026-05-08, currently waiting for the manager's approval at step `approval_manager`". Without runtime, the spec is a blueprint nobody compiles.

This change introduces the runtime triad — `ProcessInstance`, `Task`, `TaskHistory` — plus the state-transition engine that drives them. It is the missing keel: every prior proposal (approver resolution, delegation, notifications, line-items forms, userTask assignees, viewers) only pays off once a flow is *running* against it.

This change is also where the contracts of the prior proposals finally meet:

- The **ActorResolver** is invoked by the runtime when spawning a Task to compute the candidate user set
- The **Delegation service** is invoked at task-creation time to transform `original_assignee` → `actual_assignee`
- The **Notification dispatcher** is invoked by state-transition hooks (`on_assign`, `on_approve`, etc.)
- The **Mustache renderer** receives the `ProcessInstance.form_data` snapshot to render templates
- **TaskHistory** is the append-only audit trail mandated for ISO 9001 / IATF 16949 compliance

This change does NOT include a frontend "case detail" screen — it ships the API + engine. UI for executing tasks (the post-onboarding everyday-user experience) lives in the existing 9 mock-up flows for demo, and in `add-process-admin-ui` (later proposal) for the admin / monitoring view.

## What Changes

### Domain entities (NEW capability `bpm-process-runtime`)

**`ProcessInstance`** — one row per case (a customer initiating a leave / purchase / expense flow):

- `Id` (Guid)
- `TenantId` (Guid)
- `SpecCode` (string, e.g., `"LEAVE"`) — flow identifier
- `SpecVersion` (int) — snapshot of `meta.flowVersion` at start
- `SpecSnapshotJson` (text) — full deep-copy of the spec.json at instance start; the source of truth for *this* instance's behavior; SPEC CHANGES AFTER INSTANCE START DO NOT APPLY
- `InitiatorUserId` (Guid, FK User)
- `Status` (enum): `Running` / `Completed` / `Cancelled` / `Errored`
- `CurrentFormDataJson` (text) — the accumulating form data (immutable per-step but new fields can be added at later userTasks)
- `StartedAt`, `CompletedAt`, `CancelledAt`, `LastActivityAt` (UTC)
- `CancelReason` (nullable string)
- `LastError` (nullable, for `Errored` status)

**`Task`** — one row per open or completed activity in an instance:

- `Id` (Guid)
- `TenantId`, `ProcessInstanceId` (FK)
- `NodeId` (string) — references a node in the spec snapshot (`task_apply`, `approval_manager`, ...)
- `NodeKind` (enum): `UserTask` / `Approval` / `Notify` / `ServiceTask` (gateway and start/end nodes don't spawn Tasks)
- `OriginalAssigneeUserId` (Guid, nullable) — what the resolver returned (one of the candidate set; for collection types it's the first user; for sets ≥ 2 we currently spawn one task per candidate, see design §3)
- `ActualAssigneeUserId` (Guid, nullable) — `OriginalAssigneeUserId` after delegation transform
- `CandidateSetJson` (text) — full set returned by resolver (for "any of these can claim" semantics)
- `Status` (enum): `Pending` / `InProgress` / `Completed` / `Cancelled` / `Skipped`
- `CreatedAt`, `ClaimedAt`, `CompletedAt`, `DueAt` (UTC, nullable; populated from spec.sla)
- `Decision` (enum, nullable): `Approve` / `Reject` / `Return` — only for Approval kind
- `Comment` (string, nullable, max 2000) — actor's note on submit / return
- `FormDataPatchJson` (text, nullable) — what fields *this* userTask added/changed; merged into instance's CurrentFormDataJson on submit

**`TaskHistory`** — append-only audit; one row per state-changing event:

- `Id` (Guid)
- `TenantId`, `ProcessInstanceId`, `TaskId` (nullable; instance-level events have null TaskId)
- `EventType` (enum): `InstanceStarted` / `TaskSpawned` / `TaskClaimed` / `TaskSubmitted` / `TaskReturned` / `ApprovalApproved` / `ApprovalRejected` / `NotificationDispatched` / `GatewayEvaluated` / `InstanceCompleted` / `InstanceCancelled` / `DelegationApplied` / `SlaWarning` / `SlaBreached`
- `ActorUserId` (Guid, nullable; system events have null actor)
- `PayloadJson` (text) — event-specific data (e.g., for `DelegationApplied`: original + actual user; for `GatewayEvaluated`: branch chosen + condition value)
- `CreatedAt` (UTC)

TaskHistory is **strictly append-only**. The repository SHALL block UPDATE and DELETE at the EF interceptor level (see design §11). This is a compliance hard requirement.

### State-transition engine

`IProcessRuntime` (in `Bpm.Application.Process.Runtime`):

- `Task<Guid> StartInstanceAsync(StartInstanceCommand cmd, CancellationToken ct)` — given (specCode, formData, initiator), look up active spec version, deep-copy spec to snapshot, create instance + spawn first Task(s), write history events.
- `Task SubmitTaskAsync(SubmitTaskCommand cmd, CancellationToken ct)` — given (taskId, formDataPatch, decision?, comment?, actorUserId), validate the actor can act (assignee match), apply form patch, transition Task to Completed, advance instance state machine: spawn next Task(s), evaluate gateways, fire notifications, possibly complete instance.
- `Task ReturnTaskAsync(ReturnTaskCommand cmd, CancellationToken ct)` — Approval kind only; sets Decision = Return, spawns a *new* Task at the previous userTask node (re-fill).
- `Task CancelInstanceAsync(CancelInstanceCommand cmd, CancellationToken ct)` — admin or initiator only; marks instance Cancelled and all open tasks Cancelled.
- `Task ClaimTaskAsync(ClaimTaskCommand cmd, CancellationToken ct)` — for tasks with `CandidateSet > 1` (e.g., `functional_members` resolved to whole HR team), the first member to claim gets the task; others' parallel rows are auto-Cancelled.

The engine is responsible for:

1. **Task spawning** — at each transition, look at outgoing edges from the just-completed node; for the next node, if it's a gateway, evaluate its `condition_expr`; if userTask/approval/notify, spawn a Task.
2. **Resolver invocation** — for userTask/approval, call `IActorResolver.Resolve(spec.userTasks[X].assignee or spec.approvals[X].approver, ctx)` to get the candidate set.
3. **Delegation transform** — for each spawned Task, call `IDelegationService.GetActiveDelegateAsync(originalAssignee, now)`; if active, set `ActualAssigneeUserId = delegate`. Record `DelegationApplied` history event when transform happens.
4. **Notification dispatch** — fire notifications matching the trigger (`on_submit` at start; `on_assign` per task spawn; `on_approve` / `on_reject` per approval submit; `on_complete` at instance complete) by calling `INotificationDispatcher.DispatchAsync` with a fresh `NotificationContext`.
5. **History writing** — every state change writes one or more `TaskHistory` rows in the same DB transaction as the state mutation. No silent transitions.

### Form data semantics

`ProcessInstance.CurrentFormDataJson` is the merged accumulator. When a userTask submits its `FormDataPatchJson`, the engine applies it shallow-merge over `CurrentFormDataJson` (later step's field with same key overrides earlier — though best practice is each userTask declares its own fields). For repeater fields, the entire array replaces (no row-level merge).

`TaskHistory.PayloadJson` for `TaskSubmitted` events captures the patch (for diff/audit). `ProcessInstance.CurrentFormDataJson` reflects the latest merged state.

### API endpoints

`bpm-svc/src/Api/Process/`:

- `POST /api/processes` — body `{ spec_code, form_data }`; current user is initiator; returns `{ instance_id, first_task_id }`
- `GET /api/processes/{id}` — full instance state (snapshot, form data, history summary, current open tasks)
- `GET /api/processes/{id}/history` — paginated TaskHistory for an instance (auth: initiator or any task assignee in the instance or admin)
- `POST /api/processes/{id}/cancel` — body `{ reason }`; auth: initiator or admin
- `GET /api/tasks/mine?status=open|completed|all&limit=50` — current user's tasks
- `GET /api/tasks/{id}` — single task with merged form snapshot
- `POST /api/tasks/{id}/claim` — pool tasks with multiple candidates
- `POST /api/tasks/{id}/submit` — body `{ form_data_patch?, decision?, comment? }`
- `POST /api/tasks/{id}/return` — body `{ comment }`; Approval kind only

### Spec snapshot — version isolation

When `StartInstanceAsync` runs, it deep-copies the input `spec.json` to `ProcessInstance.SpecSnapshotJson`. All subsequent task spawning, condition evaluation, resolver lookups, notification dispatching for *this instance* read from the snapshot — NOT the live `specs-incoming/` file or any future ProcessDefinition table. This guarantees:

- Editing a spec mid-instance does NOT affect the running instance
- Two instances of the same flow at different versions execute independently and correctly
- Compliance-friendly: the audit answers "which spec version was this case run against" with the literal JSON

The snapshot is large (a typical flow ~10-20 KB JSON). For SQLite POC this is fine; when we move to Postgres we'll consider compression. Future work, not this proposal.

### Hook integration with existing capabilities

**ActorResolver** (`bpm-workflow-resolver`): runtime calls `IActorResolver.Resolve(actorRef, ctx)` where `ctx.initiator_user_id = instance.InitiatorUserId`, `ctx.form_data = instance.CurrentFormDataJson`, `ctx.current_approver_user_id = (the active approval's actor, if relevant)`. Resolver does not need modification — it already accepts this shape.

**Delegation** (`bpm-delegation`): runtime calls `IDelegationService.GetActiveDelegateAsync(originalAssignee, now)` for every spawned UserTask/Approval task. If a delegation is returned, `ActualAssigneeUserId = delegation.DelegateUserId` and a `DelegationApplied` history row is written.

**Notification engine** (`bpm-notification-engine`): runtime calls `INotificationDispatcher.DispatchAsync` once per notification spec matching the trigger event. The dispatcher looks at `notification.trigger` to decide whether the current event matches. The runtime passes `NotificationContext` populated with the fresh runtime state including the *post-delegation* `actual_assignee` user IDs.

**Auth / RBAC** (`bpm-roles-and-permissions`): task action permissions:

- Only the `ActualAssigneeUserId` (post-delegation) can submit / claim / return a task
- The `InitiatorUserId` can read instance state and history at any time
- `tenant_admin` role can cancel instances and read all
- `flow_admin:<flow_code>` can intervene on any task in that flow

### Out of scope (future changes)

- Pre-existing flow-level rules table (`ProcessDefinition` / `ProcessVersion` editing UI) — runtime reads from `specs-incoming/` files for now; full version-management editor is a later change
- SLA timer + escalation actions — separate `add-sla-timer-escalation` change (this proposal's `DueAt` field is populated but no engine actions on breach)
- Service tasks invoking external systems — `ServiceTask` enum exists but engine just records "service task placeholder" history event for now; HTTP/MCP integration is later
- Comment threads / @mentions — separate `add-comments-and-rejection-feedback` change (this proposal supports a single Comment string per task; no threading)
- Outbound webhooks (`on_complete` → customer system) — separate `add-outbound-webhooks` change
- PDF export of completed cases — separate `add-pdf-export` change
- Real-time progress subscription (WebSocket / SignalR) — clients poll for now
- Bulk actions (cancel multiple instances) — admin tooling
- Process simulator (dry-run) — separate `add-process-admin-ui` change
- Migration / replay tools (replay history to debug an instance)
- File upload integration in form data — separate `add-file-storage` change (FormFieldType=file is honored at submit time but file binary lives elsewhere)

## Capabilities

### New Capabilities

- `bpm-process-runtime` — runtime entities (ProcessInstance, Task, TaskHistory), state-transition engine (IProcessRuntime), spec snapshot, claim/submit/return/cancel actions, append-only history, REST API.

### Modified Capabilities

- `bpm-actor-dsl` — clarify that the `ActorRef` resolution context at runtime is built from `ProcessInstance.CurrentFormDataJson` + initiator + current approver/assignee fields populated by the runtime.
- `bpm-notification-engine` — clarify hook integration: runtime calls dispatcher at trigger events; ProcessInstance-aware context replaces the dev-fire shape.
- `bpm-delegation` — clarify that `GetActiveDelegateAsync` is called from the runtime at task-creation time and that `DelegationApplied` history events are emitted.
- `bpm-form-stepper` — no UI change in this proposal; clarify that the spec authored by StepForms / StepApprovers / StepNotify / StepSla is the runtime input.

## Impact

- **bpm-svc/src/Domain/Entities/Process/**: ProcessInstance, Task, TaskHistory entities + enums (InstanceStatus, TaskStatus, NodeKind, EventType, Decision)
- **bpm-svc/src/Application/Process/Runtime/**: IProcessRuntime, ProcessRuntime, command records (StartInstance / SubmitTask / ReturnTask / ClaimTask / CancelInstance), state-machine helpers
- **bpm-svc/src/Persistence/Configurations/Process/**: EF configs, migration `AddProcessRuntime` (3 new tables, indexes)
- **bpm-svc/src/Persistence/Interceptors/**: extend AuditSaveChangesInterceptor (or new TaskHistoryAppendOnlyInterceptor) to reject UPDATE/DELETE on TaskHistory
- **bpm-svc/src/Api/Process/**: ProcessController (start/cancel/get/history), TaskController (mine/get/claim/submit/return)
- **bpm-svc/src/Application/Spec/SpecLoader.cs** (might already exist or new): loads spec.json by code; runtime calls this to get the spec to snapshot
- **bpm-ui/src/lib/process.ts**: NEW — TypeScript types + API client
- **No new frontend screens in this proposal** — the `forms/*.tsx` mock-up flows continue to demo the *shape* of the everyday-user experience; `add-process-admin-ui` will provide live monitoring and `add-form-runtime-rendering` will replace mocks with spec-driven forms
- **Demo guard**: `forms/*`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` NOT modified
- **DB migration**: 3 new tables, all indexed; no changes to existing tables
- **NuGet additions**: none (System.Text.Json sufficient for spec snapshot serialization; existing FluentValidation for command validators)
- **Test fixtures**: a new `ProcessRuntimeFixture` exercising LEAVE end-to-end (start → submit task_apply → manager approves → HR archives → complete) using seed users

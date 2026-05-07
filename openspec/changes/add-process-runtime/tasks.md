# Tasks

## 1. Domain entities

- [ ] 1.1 Create `bpm-svc/src/Domain/Entities/Process/InstanceStatus.cs` enum (Running, Completed, Cancelled, Errored)
- [ ] 1.2 Create `bpm-svc/src/Domain/Entities/Process/TaskStatus.cs` enum (Pending, InProgress, Completed, Cancelled, Skipped)
- [ ] 1.3 Create `bpm-svc/src/Domain/Entities/Process/NodeKind.cs` enum (StartEvent, EndEvent, UserTask, Approval, Gateway, Notify, ServiceTask)
- [ ] 1.4 Create `bpm-svc/src/Domain/Entities/Process/Decision.cs` enum (Approve, Reject, Return)
- [ ] 1.5 Create `bpm-svc/src/Domain/Entities/Process/HistoryEventType.cs` enum (InstanceStarted, TaskSpawned, TaskClaimed, TaskSubmitted, TaskReturned, ApprovalApproved, ApprovalRejected, NotificationDispatched, GatewayEvaluated, InstanceCompleted, InstanceCancelled, DelegationApplied, SlaWarning, SlaBreached)
- [ ] 1.6 Create `bpm-svc/src/Domain/Entities/Process/ProcessInstance.cs` (inherits AuditableEntity)
- [ ] 1.7 Create `bpm-svc/src/Domain/Entities/Process/ProcessTask.cs` (named ProcessTask to avoid collision with System.Threading.Tasks.Task)
- [ ] 1.8 Create `bpm-svc/src/Domain/Entities/Process/TaskHistory.cs`

## 2. Persistence — EF configurations + migration

- [ ] 2.1 Create `bpm-svc/src/Persistence/Configurations/Process/ProcessInstanceConfiguration.cs`; index (TenantId, Status, LastActivityAt DESC), (InitiatorUserId, StartedAt DESC), (SpecCode, SpecVersion)
- [ ] 2.2 Create `ProcessTaskConfiguration.cs`; index (ActualAssigneeUserId, Status), (ProcessInstanceId, NodeId), (Status, DueAt) WHERE Status IN ('Pending', 'InProgress')
- [ ] 2.3 Create `TaskHistoryConfiguration.cs`; index (ProcessInstanceId, CreatedAt), (TaskId), (EventType, CreatedAt)
- [ ] 2.4 Add DbSets to `BpmDbContext`: ProcessInstances, ProcessTasks, TaskHistories
- [ ] 2.5 Generate migration: `dotnet ef migrations add AddProcessRuntime`
- [ ] 2.6 Apply locally; verify with `sqlite3 bpm.db .schema "ProcessInstances"` etc.

## 3. Append-only enforcement for TaskHistory

- [ ] 3.1 Extend `AuditSaveChangesInterceptor` (or create `TaskHistoryAppendOnlyInterceptor`) to inspect ChangeTracker entries; throw `InvalidOperationException` if any TaskHistory entity has EntityState.Modified or Deleted
- [ ] 3.2 Register interceptor in `Persistence/DependencyInjection.cs`
- [ ] 3.3 Integration test: load a TaskHistory row, modify a field, call SaveChanges → expect exception
- [ ] 3.4 Integration test: attempt `Remove` on TaskHistory → expect exception

## 4. Application — runtime command records + validators

- [ ] 4.1 Create `bpm-svc/src/Application/Process/Runtime/Commands/StartInstanceCommand.cs`: SpecCode (string), FormData (JsonElement), InitiatorUserId (Guid)
- [ ] 4.2 Create `SubmitTaskCommand.cs`: TaskId, ActorUserId, FormDataPatch (JsonElement?), Decision (Decision?), Comment (string?)
- [ ] 4.3 Create `ReturnTaskCommand.cs`: TaskId, ActorUserId, Comment (string, required)
- [ ] 4.4 Create `ClaimTaskCommand.cs`: TaskId, ActorUserId
- [ ] 4.5 Create `CancelInstanceCommand.cs`: InstanceId, ActorUserId, Reason (string)
- [ ] 4.6 FluentValidation validators for each: required fields non-empty, comment max 2000 chars, ReturnTask requires non-empty comment

## 5. Application — minimal expression evaluator (until CEL)

- [ ] 5.1 Create `bpm-svc/src/Application/Process/Expressions/IExpressionEvaluator.cs`: `bool Evaluate(string expression, IReadOnlyDictionary<string, JsonElement> context)`
- [ ] 5.2 Implement `MinimalExpressionEvaluator.cs` supporting: `==`, `!=`, `>`, `>=`, `<`, `<=`, `&&`, `||`, dotted field paths (e.g., `leave.days`), numeric / string literals
- [ ] 5.3 Reject unsupported expressions (functions, lists, math) with clear error message
- [ ] 5.4 Unit tests: 12+ scenarios covering equality, comparison, AND/OR, dotted paths, type coercion (string "5" vs number 5), failure on unsupported syntax
- [ ] 5.5 Document in design.md §10 that this is a placeholder until `add-cel-expressions` lands

## 6. Application — ProcessRuntime service

- [ ] 6.1 Create `bpm-svc/src/Application/Process/Runtime/IProcessRuntime.cs` with the five methods from design.md §1
- [ ] 6.2 Create `ProcessRuntime.cs` implementation
- [ ] 6.3 Create `bpm-svc/src/Application/Process/Runtime/SpecSnapshot.cs` parsed-spec wrapper (lazy-parsed from JSON for in-memory access during a request)
- [ ] 6.4 `StartInstanceAsync`:
  - Load active spec for `cmd.SpecCode` from `specs-incoming/<tenant>/<spec_code>.json` (or future SpecLoader service)
  - Deep-copy spec to `instance.SpecSnapshotJson`
  - Validate initial form data against userTasks[0].fields if applicable
  - Insert ProcessInstance; write `InstanceStarted` history
  - Dispatch `on_submit` notifications
  - Spawn first task(s) for the node downstream of StartEvent (handle gateway-as-first-node edge case)
  - Return new instance id + first task id
- [ ] 6.5 `SubmitTaskAsync`: per pseudocode in design.md §5 — validate actor, apply patch, write history, advance state machine, dispatch on_assign / on_approve / on_reject notifications, possibly complete instance with on_complete
- [ ] 6.6 `ReturnTaskAsync`: validate actor + Approval kind; record Return decision; spawn new Task at the previous userTask node; write `TaskReturned` history; dispatch return-related notifications (none defined yet but scaffold the call site)
- [ ] 6.7 `ClaimTaskAsync`: SQL `UPDATE ... WHERE Id = ? AND Status = 'Pending'`; on success write `TaskClaimed`; for sibling candidate tasks (same NodeId, same instance, Status = Pending), bulk update to Cancelled with auto-cancellation history events
- [ ] 6.8 `CancelInstanceAsync`: validate actor (initiator or tenant_admin); set Cancelled + CancelReason; cascade-cancel all open tasks; write `InstanceCancelled` history; dispatch cancellation notification (if spec has one defined)

## 7. Hook integration

- [ ] 7.1 In `ProcessRuntime`, inject `IActorResolver`, `IDelegationService`, `INotificationDispatcher`
- [ ] 7.2 On task spawn: call resolver per-node assignee/approver; expand to one Task per candidate; for each call delegation; record DelegationApplied history if transformed
- [ ] 7.3 On state events: build `NotificationContext` from current instance state (initiator, current_approver, current_assignee, form_data) and dispatch matching notifications
- [ ] 7.4 Notification dispatch is in-transaction (insert NotificationDelivery rows, audit row) — channel adapters do NOT run synchronously; worker picks up async

## 8. API endpoints

- [ ] 8.1 Create `bpm-svc/src/Api/Process/ProcessController.cs`:
  - `POST /api/processes` — start an instance
  - `GET /api/processes/{id}` — instance + open tasks
  - `GET /api/processes/{id}/history` — paginated history
  - `POST /api/processes/{id}/cancel` — cancel
- [ ] 8.2 Create `TaskController.cs`:
  - `GET /api/tasks/mine?status=open|completed|all&limit=50` — current user's tasks
  - `GET /api/tasks/{id}` — single task
  - `POST /api/tasks/{id}/claim` — claim a pool task
  - `POST /api/tasks/{id}/submit` — submit form patch + decision
  - `POST /api/tasks/{id}/return` — return to previous userTask
- [ ] 8.3 Authorization: handlers checking `ActualAssigneeUserId == current user` for action endpoints; readers checking allowed reader roles
- [ ] 8.4 Integration tests for each endpoint (happy + permission rejection + not found + cancellation race)

## 9. Frontend — types + API client

- [ ] 9.1 Create `bpm-ui/src/lib/process.ts`:
  - TypeScript mirrors of ProcessInstance / Task / TaskHistory shapes
  - API client: `startProcess`, `getInstance`, `getInstanceHistory`, `cancelInstance`, `myTasks`, `getTask`, `claimTask`, `submitTask`, `returnTask`
- [ ] 9.2 Hook `useMyTasks(filter)` polling every 30s

## 10. End-to-end test fixture

- [ ] 10.1 Create `bpm-svc/tests/Fixtures/ProcessRuntimeFixture.cs` exercising LEAVE flow:
  - Wilson (employee) starts LEAVE instance with 5-day vacation
  - Wilson submits `task_apply` form
  - Yang (manager) sees task in his pool; claims; submits Approve
  - Mary (HR) sees `task_hr_archive`; submits archive note
  - Instance reaches Completed; verify history has full sequence
- [ ] 10.2 Variant: 8-day leave triggers gateway → VP approval; verify gateway evaluated event + VP task spawned
- [ ] 10.3 Variant: Yang has active delegation to Lin; Wilson submits leave; verify task spawns with `OriginalAssigneeUserId = Yang, ActualAssigneeUserId = Lin` + DelegationApplied history
- [ ] 10.4 Variant: cancel mid-flow; verify all open tasks Cancelled + history record

## 11. Notification integration verification

- [ ] 11.1 Boot bpm-svc with seed; start a LEAVE instance; verify `on_submit` notification dispatched (NotificationDelivery rows inserted)
- [ ] 11.2 Submit task_apply; verify `on_assign` notification dispatched to manager (post-delegation if active)
- [ ] 11.3 Manager approves; verify `on_approve` notification (if defined in spec)
- [ ] 11.4 Instance completes; verify `on_complete` to submitter

## 12. Sample spec exercising

- [ ] 12.1 Run runtime against `sample_specs/leave_v1.json` — full happy path
- [ ] 12.2 Run against `sample_specs/purchase_v1.json` — gateway-on-amount path
- [ ] 12.3 Run against `sample_specs/expense_with_threshold_v1.json` — collection actor type
- [ ] 12.4 Verify all four pre-existing sample specs execute without errors

## 13. Documentation

- [ ] 13.1 Update `bpm-svc/CLAUDE.md` with runtime architecture overview, hook invocation order, snapshot semantics
- [ ] 13.2 Update `pipeline_architecture.md` to reference runtime as the engine the multi-agent pipeline produces output for
- [ ] 13.3 Add a section to `SETUP.md` on starting a manual instance via curl (for ops testing)

## 14. End-to-end verification

- [ ] 14.1 `dotnet build` clean
- [ ] 14.2 `dotnet test` — all backend tests including the new ProcessRuntimeFixture pass
- [ ] 14.3 Apply migration on fresh `bpm.db`; schema includes ProcessInstances / ProcessTasks / TaskHistories
- [ ] 14.4 Boot service; hit `POST /api/processes` with leave spec_code + sample form; verify response includes instance_id + first_task_id
- [ ] 14.5 Login as manager (RoleSwitcher); call `GET /api/tasks/mine`; verify the leave task appears
- [ ] 14.6 Submit approval; verify next task spawns; verify TaskHistory rows present
- [ ] 14.7 **Demo guard**: `bpm-ui/src/screens/Home.tsx`, `forms/*`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts` NOT modified

## 15. Commit

- [ ] 15.1 Commit in chunks (entities + migration; expression evaluator + commands; ProcessRuntime service + hooks; API + tests; fixture + integration; docs)
- [ ] 15.2 Push via GitKraken (Claude does not push to BPM repo)

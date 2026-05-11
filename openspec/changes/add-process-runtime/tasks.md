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

- [x] 3.1 Extend `AuditSaveChangesInterceptor` (or create `TaskHistoryAppendOnlyInterceptor`) to inspect ChangeTracker entries; throw `InvalidOperationException` if any TaskHistory entity has EntityState.Modified or Deleted
- [x] 3.2 Register interceptor in `Persistence/DependencyInjection.cs`
- [x] 3.3 Integration test: load a TaskHistory row, modify a field, call SaveChanges → expect exception
- [x] 3.4 Integration test: attempt `Remove` on TaskHistory → expect exception

## 4. Application — runtime command records + validators

- [x] 4.1 Create `bpm-svc/src/Application/Process/Runtime/Commands/StartInstanceCommand.cs`: SpecCode (string), FormData (JsonElement), InitiatorUserId (Guid)
- [x] 4.2 Create `SubmitTaskCommand.cs`: TaskId, ActorUserId, FormDataPatch (JsonElement?), Decision (Decision?), Comment (string?)
- [x] 4.3 Create `ReturnTaskCommand.cs`: TaskId, ActorUserId, Comment (string, required)
- [x] 4.4 Create `ClaimTaskCommand.cs`: TaskId, ActorUserId
- [x] 4.5 Create `CancelInstanceCommand.cs`: InstanceId, ActorUserId, Reason (string)
- [x] 4.6 FluentValidation validators for each: required fields non-empty, comment max 2000 chars, ReturnTask requires non-empty comment

## 5. Application — minimal expression evaluator (until CEL)

- [~] 5.1 skipped — CelNetExpressionEvaluator covers this; design.md §10 placeholder no longer applies
- [~] 5.2 skipped — CelNetExpressionEvaluator covers this; design.md §10 placeholder no longer applies
- [~] 5.3 skipped — CelNetExpressionEvaluator covers this; design.md §10 placeholder no longer applies
- [~] 5.4 skipped — CelNetExpressionEvaluator covers this; design.md §10 placeholder no longer applies
- [~] 5.5 skipped — CelNetExpressionEvaluator covers this; design.md §10 placeholder no longer applies

## 6. Application — ProcessRuntime service

- [x] 6.1 Created `bpm-svc/src/Application/Process/Runtime/IProcessRuntime.cs` (5 methods)
- [x] 6.2 Created `ProcessRuntime` implementation in `bpm-svc/src/Persistence/Process/ProcessRuntime.cs` (Persistence layer because it needs `AppDbContext`)
- [x] 6.3 Created `SpecSnapshot` lazy view in `bpm-svc/src/Application/Process/Runtime/SpecSnapshot.cs`
- [x] 6.4 `StartInstanceAsync` — loads spec via `ISpecLoader`, snapshots JSON, inserts instance, writes `InstanceStarted`, spawns first task downstream of StartEvent (handles gateway-first / endEvent-first), dispatches `on_submit`
- [x] 6.5 `SubmitTaskAsync` — opens transaction, validates actor + status, applies patch (shallow merge), writes `TaskSubmitted` + `ApprovalApproved`/`ApprovalRejected` as appropriate, advances; sibling-aware (collection mode v1 = wait for all)
- [x] 6.6 `ReturnTaskAsync` — Approval-only, walks predecessor edges back to nearest userTask, spawns task there, writes `TaskReturned`
- [x] 6.7 `ClaimTaskAsync` — atomic `ExecuteUpdateAsync` (Pending→InProgress); auto-cancels sibling pool tasks; writes `TaskClaimed`
- [x] 6.8 `CancelInstanceAsync` — initiator-only (admin override TODO), cascade-cancels open tasks, writes `InstanceCancelled`

## 7. Hook integration

- [x] 7.1 `ProcessRuntime` ctor injects `IActorResolver`, `IDelegationService`, `INotificationDispatcher`, `IExpressionEvaluator`, `ISpecLoader`
- [x] 7.2 Per-spawn: resolver call → expand candidates → one Task per candidate → delegation per candidate → `DelegationApplied` history when transformed
- [x] 7.3 `NotificationContext` record carries (instance, spec, initiator, formData, approvers, assignees); dispatched at `on_submit` / `on_assign` / `on_complete` / `on_cancel`
- [x] 7.4 Dispatch is invoked inside the same `SaveChangesAsync` transaction; v1 dispatcher is a `LoggingNotificationDispatcher` stub (`add-notification-engine` will replace with NotificationDelivery rows)

## 8. API endpoints

- [x] 8.1 Create `bpm-svc/src/Api/Process/ProcessController.cs`:
  - `POST /api/processes` — start an instance
  - `GET /api/processes/{id}` — instance + open tasks
  - `GET /api/processes/{id}/history` — paginated history (cursor = `CreatedAt|Id`)
  - `POST /api/processes/{id}/cancel` — cancel
- [x] 8.2 Create `TaskController.cs`:
  - `GET /api/tasks/mine?status=open|completed|all&limit=50` — current user's tasks
  - `GET /api/tasks/{id}` — single task with merged form snapshot
  - `POST /api/tasks/{id}/claim` — claim a pool task
  - `POST /api/tasks/{id}/submit` — submit form patch + decision
  - `POST /api/tasks/{id}/return` — return to previous userTask
- [x] 8.3 Authorization: action endpoints rely on `ProcessRuntime`'s assignee/initiator checks (Forbidden surfaced via exception filter); read endpoints (`GetById`, `GetHistory`, `GetTask`) reject non-initiator/non-assignee with `ForbiddenException`. Tenant-admin override is a v2 TODO. Helper extracted to `Bpm.Api.Common.BpmControllerBase`.
- [x] 8.4 Controller-layer integration tests in `bpm-svc/tests/Bpm.Tests/Api/Process/`: 17 facts covering happy path (start/get/history/cancel/mine/get task/submit/return/claim) + permission rejection (cross-user GetById, GetTask, Submit) + cancellation race (concurrent claim) + bad input (missing specCode, invalid decision, empty cancel reason, empty return comment).

## 9. Frontend — types + API client

- [ ] 9.1 Create `bpm-ui/src/lib/process.ts`:
  - TypeScript mirrors of ProcessInstance / Task / TaskHistory shapes
  - API client: `startProcess`, `getInstance`, `getInstanceHistory`, `cancelInstance`, `myTasks`, `getTask`, `claimTask`, `submitTask`, `returnTask`
- [ ] 9.2 Hook `useMyTasks(filter)` polling every 30s

## 10. End-to-end test fixture

- [x] 10.1 `ProcessRuntimeE2EFixture.Leave_happy_5_day_completes_via_manager_then_hr` — Wilson 5-day leave, Yang approves, Mary archives, instance Completed + history sequence asserted
- [x] 10.2 `Leave_8_day_routes_through_vp_then_hr` — gateway `e4` (days >= 7) → approval_vp (Chen, dept head of ENG) → archive → end
- [x] 10.3 `Leave_delegation_redirects_manager_approval_to_delegate` — `StubReplaceDelegationService(Yang → Lin)`; manager-approval task spawns with `OriginalAssigneeUserId=Yang, ActualAssigneeUserId=Lin` + DelegationApplied history
- [x] 10.4 `Leave_cancel_mid_flow_cancels_open_tasks` — cancel during manager-approval pending; all open tasks Cancelled + InstanceCancelled history

## 11. Notification integration verification

- [x] 11.1 `Notifications_dispatcher_consulted_for_on_submit_even_when_no_match` — `RecordingNotificationDispatcher` records one `on_submit` call with 0 matched ids (leave_v1 defines none) — proves dispatcher consulted
- [x] 11.2 `Notifications_on_assign_dispatched_with_manager_notification_id` — submit task_apply → `on_assign` call contains `notify_assign_manager`
- [x] 11.3 `Notifications_on_approve_dispatched_even_when_no_match` — manager approves; assert no `on_approve` trigger fired (runtime emits next on_assign instead; leave_v1 has no on_approve template)
- [x] 11.4 `Notifications_on_complete_dispatched_when_instance_completes` — full happy path completes → `on_complete` call with `notify_complete`

## 12. Sample spec exercising

- [x] 12.1 Covered by 10.1 (LEAVE happy path via `leave_v1.json`)
- [x] 12.2 `Purchase_50k_routes_through_finance_then_purchase_exec` + `Purchase_200k_requires_manager_finance_and_ceo` exercise `purchase_v1.json` gateway-on-amount via Yang/Jin/Sandy
- [x] 12.3 `Expense_small_amount_routes_to_manager_then_finance_review` (expr-only path), `Expense_medium_amount_collection_any_two_of_three_advances` (mode='any' min_approvals=2 — runtime extended to support early-finish + sibling auto-cancel), `Expense_large_amount_collection_all_three_required` (mode='all')
- [x] 12.4 `All_sample_specs_parse_with_SpecSnapshot` — loads & parses all three sample specs, asserts non-empty flowCode, startNode, edges

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

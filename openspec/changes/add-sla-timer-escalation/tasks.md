# Tasks

## 1. SLA calculator

- [ ] 1.1 Create `bpm-svc/src/Application/Sla/ISlaCalculator.cs` and `SlaCalculator.cs`
- [ ] 1.2 Implement `ParseDuration(string) -> TimeSpan` supporting `Nd`, `Nh`, `Nm` (minutes)
- [ ] 1.3 Implement `IBusinessCalendar` minimal default (Mon-Fri 09:00-18:00, configurable via env)
- [ ] 1.4 Implement `ComputeDueAt(spawnTime, NodeSLA, IBusinessCalendar) -> DateTime`
- [ ] 1.5 Unit tests: 12+ scenarios covering business-hours wrap, weekend skip, multi-day, edge of business day

## 2. Process runtime integration — populate DueAt on task spawn

- [ ] 2.1 Update `bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs` task-spawn code path
- [ ] 2.2 If spec.sla.perNode contains the task's NodeId, call `SlaCalculator.ComputeDueAt`; assign `task.DueAt`
- [ ] 2.3 Tests: spawn a task whose spec has `sla.perNode[node].duration = "8h"`; verify DueAt = spawn + 8h business-hours-aware

## 3. Escalation handlers

- [ ] 3.1 Create `bpm-svc/src/Application/Sla/IEscalationActionExecutor.cs` interface
- [ ] 3.2 Create per-action handlers: `NotifyEscalation.cs`, `ReassignEscalation.cs`, `EscalateOneLevelEscalation.cs`, `AutoApproveEscalation.cs`, `AutoRejectEscalation.cs`
- [ ] 3.3 NotifyEscalation: dispatch notifications matching `on_sla_breach` for the breached task
- [ ] 3.4 ReassignEscalation: cancel original task with reason `SlaReassigned`; spawn new task with `escalation.target` ActorRef; respect delegation
- [ ] 3.5 EscalateOneLevelEscalation: walk one hop up `IOrgChartReader.WalkManagerChain` from original assignee; if no parent, fallback to notify
- [ ] 3.6 AutoApproveEscalation: complete the task with Decision = Approve, actorUserId = SystemUserId; runtime advances state machine
- [ ] 3.7 AutoRejectEscalation: same with Decision = Reject
- [ ] 3.8 EscalationActionExecutor dispatches to per-action handler based on `escalation.action` enum

## 4. SLA timer job

- [ ] 4.1 Create `bpm-svc/src/Infrastructure/Sla/SlaTimerJob.cs` (BackgroundService)
- [ ] 4.2 Loop: every `BPM_SLA_TIMER_INTERVAL_SEC` seconds (default 60); query `ProcessTasks` where `Status IN (Pending, InProgress)` AND `DueAt IS NOT NULL`
- [ ] 4.3 For each task:
  - Compute `percentElapsed = (now - spawnAt) / (dueAt - spawnAt) * 100` clamped 0-100
  - If 50 ≤ percentElapsed < 100 AND no SlaWarning event for this task yet: write SlaWarning history; dispatch `on_sla_warning` notifications
  - If percentElapsed >= 100 AND no SlaBreached event yet: write SlaBreached history; invoke EscalationActionExecutor with the spec's escalation block
- [ ] 4.4 Avoid double-firing: query TaskHistory by (TaskId, EventType) before emitting
- [ ] 4.5 Register hosted service in Program.cs gated on `BPM_SLA_TIMER=on`

## 5. Notification trigger extension

- [ ] 5.1 Extend `NotificationTrigger` enum with `OnSlaWarning` (alongside existing OnSlaBreach)
- [ ] 5.2 Update spec_schema.md §2.6 trigger list
- [ ] 5.3 Update wizard StepNotify trigger dropdown
- [ ] 5.4 Update prompt_template_v1.md with worked examples for both triggers

## 6. SLA dashboard endpoints

- [ ] 6.1 Create `bpm-svc/src/Api/Sla/SlaController.cs`:
  - `GET /api/sla/at-risk?within_hours=8` — open tasks due within X hours; auth: any reader role
  - `GET /api/sla/breached?since=ISO8601` — recently breached
  - `GET /api/sla/per-spec-stats?spec_code=&period=30d` — aggregates: breach rate, p50/p95 resolution time, count
  - `POST /api/tasks/{id}/escalate` — admin only; manual trigger; same code path as auto escalate_one_level
- [ ] 6.2 Integration tests for each

## 7. System user seed

- [ ] 7.1 Update `OrgFixture.cs` to seed a `system` User row with id `00000000-0000-0000-0000-000000000001`, email `system@bpm`, IsActive = false (cannot log in), FullName = "System"
- [ ] 7.2 Auto-approve / auto-reject use this user as actor

## 8. Sample specs

- [ ] 8.1 Update `sample_specs/leave_v1.json` SLA: manager_approve = `{ duration: "8h", businessHoursOnly: true, escalation: { after: "8h", action: "notify" } }`
- [ ] 8.2 Add a sample where escalation action = `escalate_one_level` (e.g., a high-stakes purchase flow)
- [ ] 8.3 Add a sample where action = `auto_approve` for low-stakes (e.g., team announcement requiring HR confirmation; defaults to approve after 3 days)

## 9. Tests

- [ ] 9.1 Unit tests on each escalation handler
- [ ] 9.2 Integration: spawn a task, fast-forward clock 4h (50% of 8h), trigger SLA timer, verify SlaWarning history + on_sla_warning notification dispatched
- [ ] 9.3 Integration: fast-forward 8h, trigger SLA timer, verify SlaBreached history + escalation action ran (per spec); verify second tick does NOT re-fire
- [ ] 9.4 Integration with delegation: original assignee Yang has delegation to Lin; SLA breach with action = escalate_one_level walks to Yang's manager (delegation does NOT enter the escalation chain — escalation is by org structure, not delegation)
- [ ] 9.5 Auto-approve: task breaches, action = auto_approve; verify Status = Completed with Decision = Approve, actorUserId = system user; instance advances correctly

## 10. End-to-end verification

- [ ] 10.1 `dotnet build` clean
- [ ] 10.2 All tests pass
- [ ] 10.3 Boot service with `BPM_SLA_TIMER_INTERVAL_SEC=2`; spawn a task with 1-minute SLA; observe SlaWarning at ~30s, SlaBreached at ~60s, escalation action executed; verify TaskHistory rows
- [ ] 10.4 GET /api/sla/at-risk; verify the task appears with countdown
- [ ] 10.5 GET /api/sla/breached; verify the breached task is listed
- [ ] 10.6 **Demo guard**: 9 mock-up forms, Home, Search, Report, lib/workflow.ts NOT modified

## 11. Commit

- [ ] 11.1 Commit in chunks (calculator + DueAt; handlers; timer job; trigger ext; endpoints; samples + tests; verification)
- [ ] 11.2 Push via GitKraken

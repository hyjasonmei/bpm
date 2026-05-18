## ADDED Requirements

### Requirement: SLA timer scans open tasks for breaches

The system SHALL run an `SlaTimerJob` BackgroundService that polls every minute (configurable) and inspects every `ProcessTask` with `Status IN (Pending, InProgress)` and a non-null `DueAt`. For each task, the job MUST compute `percentElapsed = (now - createdAt) / (dueAt - createdAt) * 100`. When `percentElapsed >= 50` and no `SlaWarning` history event exists for the task, a `SlaWarning` event SHALL be written and a `on_sla_warning` notification fired (if any spec defines one). When `percentElapsed >= 100` and no `SlaBreached` event exists, a `SlaBreached` event SHALL be written and the configured escalation action SHALL execute.

The job MUST be idempotent: re-running on the same task does not duplicate history events. Idempotency is enforced by checking TaskHistory by `(TaskId, EventType)` before emitting.

#### Scenario: Warning fires at 50%

- **GIVEN** a task spawned at T0 with DueAt = T0 + 8h
- **WHEN** the SLA timer runs at T0 + 4h01m
- **THEN** an SlaWarning history row is written; `on_sla_warning` notifications dispatch

#### Scenario: Breach fires at 100%

- **WHEN** the SLA timer runs at T0 + 8h01m and the task is still open
- **THEN** an SlaBreached history row is written; the configured escalation action runs

#### Scenario: Idempotent re-runs

- **GIVEN** an SlaBreached history row already exists for task T1
- **WHEN** the SLA timer runs again
- **THEN** no second SlaBreached row is written; no duplicate escalation occurs

### Requirement: DueAt is computed at task spawn from sla.perNode

The runtime SHALL set `task.DueAt` at task-spawn time by reading `spec_snapshot.sla.perNode[task.NodeId]` and calling `ISlaCalculator.ComputeDueAt(spawnTime, nodeSla, businessCalendar)`. If no SLA is defined for the node, `DueAt` remains null and the task is excluded from the timer job's scan.

#### Scenario: DueAt set when SLA defined

- **GIVEN** spec.sla.perNode["approval_manager"] = `{ duration: "8h", businessHoursOnly: true }`
- **WHEN** runtime spawns a task with NodeId = "approval_manager" at 2026-05-08 14:00 UTC
- **THEN** task.DueAt is computed using the calculator (including business-hours wrap if applicable)

#### Scenario: No SLA leaves DueAt null

- **GIVEN** no SLA entry for a node
- **WHEN** runtime spawns the task
- **THEN** task.DueAt = null; the SLA timer job ignores it

### Requirement: Five escalation actions are supported

The system SHALL support five escalation actions, all dispatched by `IEscalationActionExecutor`:

- `notify` — fire `on_sla_breach` notifications (existing trigger), no other state change
- `reassign` — cancel original task with reason `SlaReassigned`; spawn new task with `escalation.target` ActorRef as assignee
- `escalate_one_level` — walk one hop up the manager chain from the original assignee; spawn new task there; if no parent, fall back to `notify`
- `auto_approve` (Approval kind only) — set Decision = Approve, Status = Completed, actorUserId = system user; runtime advances state machine
- `auto_reject` (Approval kind only) — set Decision = Reject, Status = Completed, actorUserId = system user

`auto_approve` / `auto_reject` MUST be rejected by the validator if the spec applies them to a non-Approval node. The system user SHALL be a seeded User with id `00000000-0000-0000-0000-000000000001`, IsActive = false, FullName = "System".

#### Scenario: Reassign cancels original, spawns new

- **GIVEN** task T1 breaches with action = reassign and target = `{ type: 'role', code: 'VP' }`
- **WHEN** the executor runs
- **THEN** T1.Status = Cancelled with reason "SlaReassigned"; a new task T2 is spawned with assignee resolved from `role:VP`

#### Scenario: Auto-approve completes the task as system user

- **GIVEN** an Approval task breaches with action = auto_approve
- **WHEN** the executor runs
- **THEN** task.Status = Completed; task.Decision = Approve; task.ActorUserId = system user id; SlaBreached history payload includes `action_taken: "auto_approve", reason: "sla_breach_auto_decision"`; instance advances to next node

#### Scenario: Escalate-one-level falls back to notify if no parent

- **GIVEN** the original assignee is the CEO (manager_id = null)
- **WHEN** action = escalate_one_level executes
- **THEN** the executor falls back to `notify` action and writes a warning to logs

### Requirement: Manual escalation endpoint for admins

The system SHALL expose `POST /api/tasks/{id}/escalate` for admins to manually invoke `escalate_one_level` regardless of the task's SLA state. Auth: requires admin role. The endpoint enforces the same fall-back rules as the automated path.

#### Scenario: Admin manual escalate

- **WHEN** an admin calls `POST /api/tasks/{id}/escalate`
- **THEN** the executor invokes EscalateOneLevel for that task; original task cancelled; new task spawned

### Requirement: SLA dashboard endpoints

The system SHALL expose:

- `GET /api/sla/at-risk?within_hours=N` — open tasks where `DueAt - now < N hours`; sorted by DueAt ASC; auth: tenant_admin / flow_admin / process readers
- `GET /api/sla/breached?since=ISO8601` — tasks with SlaBreached history events after the given timestamp; sorted by event time DESC
- `GET /api/sla/per-spec-stats?spec_code=&period=30d` — aggregate metrics: total tasks, breached count, breach rate %, average resolution time, p50, p95

#### Scenario: At-risk excludes completed

- **GIVEN** task T1 is completed at T+4h; task T2 is open with DueAt = T+8h; current time = T+6h
- **WHEN** GET `/api/sla/at-risk?within_hours=4`
- **THEN** the response includes T2 only (not T1)

#### Scenario: Breached lists recently overdue

- **GIVEN** task T3 had SlaBreached event at T+8h
- **WHEN** GET `/api/sla/breached?since=T+0`
- **THEN** T3 appears with breach details

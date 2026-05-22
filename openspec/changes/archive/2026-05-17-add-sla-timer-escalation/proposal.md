## Why

`spec_schema.md` §2.7 defines `SLA` and `NodeSLA` since v1.0:

- `duration` (e.g., `"8h"`, `"3d"`)
- `businessHoursOnly` flag
- `escalation` block with `after` + `action` (`notify` | `reassign` | `escalate_one_level` | `auto_approve` | `auto_reject`) + optional `target` ActorRef

The wizard's `StepSla` already collects all of this, sample specs already use it. But:

- No timer engine watches running tasks
- `Task.DueAt` is populated by `add-process-runtime` but nothing acts on it
- Escalation actions are defined but no implementation
- `on_sla_breach` notification trigger is in the schema but never fires

For SME flows the partner showed, SLA is a marketing differentiator: "若主管 8 工時內未簽會自動通知副總". Without runtime, that is fiction.

## What Changes

### Timer engine (NEW capability `bpm-sla-timer`)

**`SlaTimerJob`** (BackgroundService): every minute, scans `ProcessTasks` where `Status IN (Pending, InProgress)` and `DueAt IS NOT NULL`. For each due-or-overdue Task it consults the spec snapshot's `sla.perNode[task.NodeId]` definition, decides whether the breach threshold has been crossed, and applies the configured `escalation.action`. Per-task: write an `SlaWarning` history row when 50% of duration is reached, an `SlaBreached` history row when 100% is reached.

**Escalation actions**:

- `notify` — fire any notification spec with `trigger = on_sla_breach` (delegated to existing notification engine)
- `reassign` — invoke `IActorResolver.Resolve(escalation.target, ctx)`; cancel the original task, spawn a new one with the new candidates; respect delegation
- `escalate_one_level` — special case of reassign: target is `expr:submitter.manager.manager` if originalAssignee is at `submitter.manager`, else next manager up. Uses `IOrgChartReader.WalkManagerChain` to find the next level
- `auto_approve` — for Approval-kind tasks: set Decision = Approve, Status = Completed with actorUserId = `system` (audit shows automated); advance state machine
- `auto_reject` — same shape with Decision = Reject

**`DueAt` calculation** at task spawn:

- Parse `duration` ("8h", "3d", "30m") into a TimeSpan
- If `businessHoursOnly = true`: skip non-business hours when adding (uses calendar from `add-calendar-and-business-hours` once landed; until then, simple Mon-Fri 09:00-18:00 default)
- Set `task.DueAt = now + computedTimeSpan`

### Pre-breach warnings

50% threshold: an `SlaWarning` history event + a notification with internal trigger `on_sla_warning` (NEW trigger added to the enum). Defaults are off — the spec author opts in by adding a notification with this trigger.

### Notification trigger extension

Add `on_sla_warning` and `on_sla_breach` as official triggers (the schema already lists `on_sla_breach`; this change makes them functional). Update `prompt_template_v1.md` examples.

### History events

`TaskHistory.HistoryEventType` enum already has `SlaWarning` and `SlaBreached` (added in `add-process-runtime`). This change extends the runtime to actually emit them, with payload:

- `SlaWarning`: `{ task_id, node_id, due_at, percent_elapsed: 50 }`
- `SlaBreached`: `{ task_id, node_id, due_at, percent_elapsed: 100, action_taken: "notify"|"reassign"|"escalate_one_level"|"auto_approve"|"auto_reject", details }`
- For escalate: `{ from_user, to_user, level }`
- For auto-approve / auto-reject: `{ decided: "Approve"|"Reject", reason: "sla_breach_auto_decision" }`

### API: SLA dashboard endpoints

For System Admin / Process Admin (UIs in later changes; APIs ship now):

- `GET /api/sla/at-risk?within_hours=8` — open tasks whose `DueAt - now < within_hours`
- `GET /api/sla/breached?since=ISO8601` — recently breached
- `GET /api/sla/per-spec-stats?spec_code=LEAVE&period=30d` — breach rate per spec, average resolution time, p50/p95 latency

### Manual escalation

`POST /api/tasks/{id}/escalate` — admin-only manual trigger; same code path as automated `escalate_one_level`.

### Configuration

- `BPM_SLA_TIMER=on|off` (default `on` in dev/prod, `off` in tests)
- `BPM_SLA_TIMER_INTERVAL_SEC` (default 60) — for tests we set to 1
- `BPM_SLA_DEFAULT_BUSINESS_HOURS_START=09`, `END=18` — defaults if calendar capability not loaded

### Out of scope (future changes)

- Customer-defined business calendar / per-tenant holidays — `add-calendar-and-business-hours` handles
- Escalation chains (escalate twice if first escalation also breaches) — single-hop only in v1
- Per-actor SLA overrides ("VP gets 48h to approve, others 8h")
- SLA performance dashboards in UI — admin UI later
- SLA-aware delegation (delegate's clock vs original's clock) — clock continues unbroken across delegation in v1
- Predictive alerts ("at this rate, this task will breach in 2 hours") — analytics-grade; defer
- Pause-resume (e.g., during a return loop, do we reset the clock?) — clock continues from original spawn; document this

## Capabilities

### New Capabilities

- `bpm-sla-timer` — SlaTimerJob (BackgroundService), DueAt calculation logic, escalation action implementations (notify / reassign / escalate_one_level / auto_approve / auto_reject), at-risk / breached / stats API endpoints, manual escalation endpoint.

### Modified Capabilities

- `bpm-process-runtime` — emit SlaWarning / SlaBreached history events when timer triggers; runtime is the actor for auto-approve / auto-reject (system user); spawn escalation tasks via existing task-spawning code path.
- `bpm-notification-engine` — add `on_sla_warning` (NEW) and document `on_sla_breach` triggers; dispatcher accepts these like other triggers.

## Impact

- **bpm-svc/src/Domain/Entities/Process/HistoryEventType.cs**: already has SlaWarning + SlaBreached — extend documentation; no enum change
- **bpm-svc/src/Domain/Spec/NotificationTrigger.cs**: add `OnSlaWarning` (alongside existing OnSlaBreach)
- **bpm-svc/src/Application/Sla/ISlaCalculator.cs / SlaCalculator.cs**: parses duration strings, applies businessHoursOnly, computes DueAt
- **bpm-svc/src/Application/Sla/IEscalationActionExecutor.cs**: dispatches to per-action handlers
- **bpm-svc/src/Application/Sla/Actions/{Notify,Reassign,EscalateOneLevel,AutoApprove,AutoReject}EscalationHandler.cs**: implementations
- **bpm-svc/src/Infrastructure/Sla/SlaTimerJob.cs**: BackgroundService, polls every 60s
- **bpm-svc/src/Api/Sla/SlaController.cs**: at-risk / breached / stats / escalate
- **bpm-svc/src/Application/Process/Runtime/ProcessRuntime.cs**: when spawning tasks, populate DueAt via SlaCalculator using spec.sla.perNode
- **bpm-ui/src/lib/sla.ts**: TS types for at-risk / stats responses (ahead of admin UI proposal)
- **No DB migration** — existing fields suffice
- **No NuGet additions**
- **Demo guard**: 9 mock-up forms not modified; manage UI lands in `add-system-admin-ui`

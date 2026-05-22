# Design notes

## 1. Why a polling timer rather than scheduled events

We considered using a delayed-message pattern (e.g., Hangfire, Quartz with a job-per-task). Rejected:

- **Cancellation complexity** — when a task completes, you need to also cancel its scheduled SLA event. Polling tolerates this naturally (already-completed tasks are skipped on next poll).
- **Failure recovery** — a delayed event lost during a crash is silently lost. Polling resumes on restart from current state.
- **Operational simplicity** — one BackgroundService + one query is easy to reason about. Scheduler libraries add operational surface.

Cost: small constant per minute (one query + ≤ N escalation calls). For SME scale (10s of open tasks) this is negligible.

## 2. Why 1-minute polling interval

Trade-off:
- 1s: precise but burns 60× more queries; wastes CPU
- 60s: ample precision for human-scale SLAs (the smallest sensible SLA is ~15 minutes; 60s drift is irrelevant)
- 300s: too laggy for "8h SLA breached" alerts

60s is the sweet spot. Configurable via env for tests.

## 3. Calendar / business hours

This change ships a *minimal* business-hours model: Mon-Fri 09:00-18:00 in the server's timezone (env-configurable). The full calendar capability (per-tenant holidays, working hours overrides) lands in `add-calendar-and-business-hours`. When that proposal lands, this engine swaps the implementation behind `IBusinessCalendar` interface — no behavior change for specs that don't use businessHoursOnly.

## 4. DueAt calculation algorithm

```
ComputeDueAt(spawnTime, sla):
  duration = ParseDuration(sla.duration)  // "8h" -> 8 hours, "3d" -> 3 days, "30m" -> 30 minutes
  if not sla.businessHoursOnly:
    return spawnTime + duration

  // Business hours: skip weekends and out-of-hours
  remaining = duration
  cursor = spawnTime
  while remaining > 0:
    nextBusinessHour = NextBusinessHourAfter(cursor)
    nextBusinessHourEnd = NextBusinessHourEnd(cursor)
    chunk = min(remaining, nextBusinessHourEnd - cursor)
    cursor = cursor + chunk
    remaining -= chunk
  return cursor
```

Edge cases tested:
- Task spawned at 17:30 with 8h SLA + business-hours-only → DueAt = next day 16:30 (30 min today + 7.5 hr next day)
- Task spawned Friday 10:00 with 16h SLA → DueAt = Tuesday 10:00
- Holiday in the middle (deferred to calendar capability)

## 5. Action: reassign — preserving original assignee for audit

When SLA breaches and action = reassign, we don't *delete* the original task — we set its Status = Cancelled with reason `SlaReassigned` and spawn a new task. Both rows persist, both are visible in TaskHistory. The original assignee's clock stops; the new assignee gets a fresh DueAt computed from the new SLA (or the same SLA depending on spec — design decision: same SLA).

## 6. Action: escalate_one_level

The semantics: walk up the manager chain by one hop from the *original* assignee.

- Original assignee = Wilson, manager = Yang, grand-manager = Lin
- Yang's task breaches; escalate_one_level walks to Lin
- New task spawned with assignee = Lin (subject to delegation transform)
- Original Yang task: Cancelled with reason `SlaEscalated`

If the original assignee already has no manager (CEO), escalation has no target → fallback action: notify only, log warning.

## 7. Action: auto_approve / auto_reject

Used by customers who say "if no one approves within 7 days, default to approve" (rare but real for low-stakes flows like simple announcement requests).

Implementation: same as a regular Approval submit, but `actorUserId = system_user_id` (a special UUID seeded in the OrgFixture as `00000000-0000-0000-0000-000000000001`). TaskHistory captures `actorUserId = system, payload.reason = "sla_breach_auto_decision"`. Auditors see "this was an automated decision".

The system user is intentionally not a real account; never logged in, never has direct task assignments.

## 8. Pre-breach warning at 50%

Why 50% specifically? Two reasons:

- Gives the assignee a useful nudge ("clock half done, please act") well before the breach
- Statistically, half-life is the largest single mass-action point — if 50% of overdue tasks are still pending at 50%, that's a strong signal humans aren't acting

Configurable per-spec? Future feature. Default 50% is hardcoded for v1; document in tasks.md.

## 9. SLA on collection-mode tasks

Tasks spawned from a `collection mode='all'` actor produce N siblings; each sibling has its own DueAt. Per design: each sibling tracks independently. If 2 of 3 are completed and 1 breaches at 100%, escalation fires for that one sibling only.

For `mode='any'` collections: when one sibling completes, the others auto-cancel; their SLA timers are moot.

## 10. Open questions

- **Reset on return**: when an Approval task returns to a userTask, the next userTask spawn gets a fresh DueAt? Or the original DueAt is inherited as a "stricter" deadline? V1: fresh DueAt at every spawn. Simple.
- **Pause during cancellation review**: not yet relevant; cancellation is admin-only and immediate.
- **Customer override for action**: can a customer add a custom escalation action like "post to Teams + reassign"? Currently action is enum; extension would require a plugin model. Defer.
- **Holiday awareness**: explicitly deferred to `add-calendar-and-business-hours`. v1 treats every Mon-Fri 09:00-18:00 as business hours.

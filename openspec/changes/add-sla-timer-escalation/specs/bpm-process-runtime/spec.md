## ADDED Requirements

### Requirement: Runtime populates DueAt from spec.sla on task spawn

The `ProcessRuntime` SHALL, when spawning a UserTask or Approval task, look up `spec_snapshot.sla.perNode[task.NodeId]`. When an entry exists, the runtime MUST call `ISlaCalculator.ComputeDueAt(spawnTime, nodeSla, calendar)` and persist the result on `task.DueAt`. If the spec has no SLA for the node, `DueAt` remains null and the task is excluded from SLA timer scans.

#### Scenario: DueAt populated from spec snapshot

- **GIVEN** spec snapshot has `sla.perNode["approval_manager"].duration = "8h"`
- **WHEN** runtime spawns a task at the approval_manager node at 2026-05-08 10:00 UTC
- **THEN** task.DueAt = 2026-05-08 18:00 UTC (or business-hours-adjusted if `businessHoursOnly` set)

### Requirement: Auto-approve / auto-reject use system user

When the SLA timer triggers `auto_approve` or `auto_reject`, the runtime SHALL complete the task as if a regular submit happened, with `actorUserId = system user id` (`00000000-0000-0000-0000-000000000001`). The TaskHistory `TaskSubmitted` payload MUST include `reason: "sla_breach_auto_decision"`. The state machine SHALL advance normally as if a human had submitted.

#### Scenario: Auto-approve advances flow

- **GIVEN** an Approval task at the manager_approve node with action = auto_approve
- **WHEN** SLA breach triggers the action
- **THEN** the task completes; the next node spawns; the audit shows the system user as actor with sla_breach_auto_decision reason

### Requirement: Escalate_one_level uses org chart, not delegation

The `escalate_one_level` action SHALL walk up the *original assignee's* manager chain by exactly one hop using `IOrgChartReader.WalkManagerChain`. Delegation SHALL NOT enter this resolution — escalation is by formal org structure, not by who is currently covering for whom. The newly spawned escalation task may itself be subject to delegation transform (the new assignee's active delegation, if any).

#### Scenario: Escalation walks original manager chain

- **GIVEN** original assignee Yang has delegation to Lin; Yang's manager is Director Chen
- **WHEN** escalation runs
- **THEN** new task assignee = Chen (original chain walked, NOT Lin's chain)
- **AND** if Chen has an active delegation to Wu, the new task's `actual_assignee = Wu` (delegation applied at task spawn)

# bpm-delegation Specification

## Purpose
TBD - created by archiving change add-process-runtime. Update Purpose after archive.
## Requirements
### Requirement: Runtime invokes GetActiveDelegateAsync at task creation

For every `ProcessTask` spawned by the runtime (UserTask or Approval kind), the runtime SHALL call `IDelegationService.GetActiveDelegateAsync(originalAssigneeUserId, now)`. If a delegation is returned, the runtime MUST set `task.ActualAssigneeUserId = delegation.DelegateUserId` and write a `DelegationApplied` TaskHistory row in the same DB transaction. If no delegation is returned, `actual_assignee_id = original_assignee_id`.

The runtime SHALL apply at most one delegation hop — recursion (looking up the delegate's own delegations) is forbidden, preventing infinite loops in mutual-delegation cycles.

#### Scenario: Delegation transforms assignee

- **GIVEN** Yang has active delegation to Lin
- **WHEN** runtime spawns a Task originally for Yang
- **THEN** task.ActualAssigneeUserId = Lin; a DelegationApplied history row records the transform with `{ original: Yang, actual: Lin, delegationId }`

#### Scenario: No delegation, no transform

- **GIVEN** Yang has no active delegation
- **WHEN** runtime spawns a Task for Yang
- **THEN** task.OriginalAssigneeUserId = Yang, task.ActualAssigneeUserId = Yang; no DelegationApplied row

#### Scenario: Cycle does not recurse

- **GIVEN** Wilson → Yang and Yang → Wilson (mutual delegation)
- **WHEN** runtime spawns a Task for Wilson
- **THEN** ActualAssigneeUserId = Yang (one hop); the runtime does NOT continue to Wilson; no infinite loop

#### Scenario: Inactive delegate falls back

- **GIVEN** Yang has active delegation to Lin, Lin.IsActive = false
- **WHEN** runtime spawns a Task for Yang
- **THEN** task.ActualAssigneeUserId = Yang (fallback); DelegationApplied row with `{ fallbackReason: 'delegate_inactive' }`

### Requirement: Notification dispatcher does NOT apply delegation to recipient resolution

The notification engine SHALL resolve `actor`-wrapped recipients via the standard `IActorResolver` without applying delegation to the resolved user set. Delegation transforms only Task assignees, not notification recipients. The runtime's `current_assignee` / `current_approver` recipient types resolve from `NotificationContext.CurrentAssigneeUserId / CurrentApproverUserId` — fields that already carry post-delegation values populated by the runtime — so the delegate naturally receives the assignment notification while the granter does not.

#### Scenario: Notification to current_assignee follows delegation

- **GIVEN** Yang has active delegation to Lin
- **AND** runtime spawns a Task for Yang (ActualAssigneeUserId = Lin) and fires on_assign
- **THEN** the notification recipient `current_assignee` resolves to Lin (because NotificationContext.CurrentAssigneeUserId = Lin)
- **AND** the in-app + email delivery rows target Lin

#### Scenario: Notification to role:HR does not follow individual delegation

- **GIVEN** HR member u_h1 has active delegation to u_h2
- **AND** notification has recipient `{ type: 'actor', inner: { type: 'role', code: 'HR' } }`
- **WHEN** dispatcher resolves
- **THEN** the recipient set includes u_h1 (NOT u_h2 — delegation is transparent at notification layer); u_h1 receives the notification even when on delegated leave


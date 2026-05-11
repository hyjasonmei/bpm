# bpm-notification-engine Specification

## Purpose
TBD - created by archiving change add-process-runtime. Update Purpose after archive.
## Requirements
### Requirement: Notification dispatch is invoked from ProcessRuntime hooks

The notification engine's dispatcher SHALL be invoked by `IProcessRuntime` at every state event whose trigger maps to a defined notification trigger (per the runtime's trigger event semantics). The dispatcher SHALL accept the runtime-provided `NotificationContext` populated with `FlowInstanceId = instance.Id`, `SubmitterUserId = instance.InitiatorUserId`, `CurrentApproverUserId = the actor of the current approval Task (post-delegation)`, `CurrentAssigneeUserId = the actor of the current userTask (post-delegation)`, and `Variables = instance.CurrentFormDataJson`.

The dispatch call SHALL execute within the same DB transaction as the state mutation; queued NotificationDelivery rows are inserted but channel adapters do not run synchronously.

#### Scenario: Dispatcher receives populated context from runtime

- **WHEN** runtime spawns a Task for Yang and fires on_assign
- **THEN** dispatcher is called with `NotificationContext { FlowInstanceId, SubmitterUserId = initiator, CurrentApproverUserId = Yang (post-delegation), Variables = current form data }`

#### Scenario: Multiple notifications dispatched in same transaction

- **GIVEN** spec defines 2 notifications matching on_complete (one to submitter, one to functional_members:hr)
- **WHEN** instance completes
- **THEN** both DispatchAsync calls execute in the same DB transaction as InstanceCompleted state mutation

### Requirement: NotificationContext.Variables sources from instance form data

The `Variables` field of `NotificationContext` SHALL be sourced from `ProcessInstance.CurrentFormDataJson` at the moment of dispatch — not from a separate variable map. Mustache templates referencing `{{form.amount}}`, `{{leave.days}}`, or any nested form path SHALL resolve against the instance's accumulated form data as of the trigger event.

#### Scenario: Template references form field

- **GIVEN** instance.CurrentFormDataJson = `{ "leave": { "days": 5 } }`
- **AND** notification body = `"已申請 {{leave.days}} 天"`
- **WHEN** dispatcher renders
- **THEN** the rendered body is `"已申請 5 天"`


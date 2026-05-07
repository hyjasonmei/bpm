## ADDED Requirements

### Requirement: Runtime constructs ActorContext from instance state

The system SHALL document that the `IActorResolver.Resolve(actorRef, ctx)` invocation at task-spawn time receives a `ResolutionContext` populated from the runtime's current `ProcessInstance` state. Specifically:

- `ctx.tenant_id = instance.TenantId`
- `ctx.initiator_user_id = instance.InitiatorUserId`
- `ctx.current_approver_user_id = the actor of the most recently completed approval Task in this instance, if any`
- `ctx.form_data = instance.CurrentFormDataJson`
- `ctx.now = the timestamp of the current state event`

These fields enable resolution of `expr:submitter.manager` (against initiator), `form_field_ref` (against form_data), and the runtime-scoped types (`current_approver` if used in approver resolution — currently used in viewer / notification scope only).

#### Scenario: Resolver receives initiator from instance

- **GIVEN** Wilson started a LEAVE instance
- **WHEN** runtime spawns task_apply's downstream approval node
- **THEN** the resolver receives ctx with `initiator_user_id = Wilson.Id`, allowing `expr:submitter.manager` to resolve against Wilson's manager

#### Scenario: Resolver receives current form data

- **GIVEN** instance.CurrentFormDataJson = `{ "amount": 80000 }`
- **WHEN** runtime evaluates a `by_amount` ActorRef on `amount_field = "amount"`
- **THEN** the resolver reads 80000 from ctx.form_data and walks the manager chain accordingly

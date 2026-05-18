## ADDED Requirements

### Requirement: NotifyRecipientResolver resolves runtime types from NotificationContext

The system SHALL provide an `INotifyRecipientResolver` service that accepts a `NotifyRecipientRef` plus `NotificationContext` and returns `Set<UserId>`. The resolver SHALL handle each variant as follows:

- `submitter` → `{ ctx.SubmitterUserId }` if non-null; empty set otherwise
- `current_approver` → `{ ctx.CurrentApproverUserId }` if non-null; empty set otherwise
- `current_assignee` → `{ ctx.CurrentAssigneeUserId }` if non-null; empty set otherwise
- `actor` → delegate to `IActorResolver.Resolve(ref.Inner, derivedActorContext)`

When a runtime context field is null (e.g., `current_approver` referenced but no approval is currently held), the resolver SHALL return an empty set with a structured `RuntimeContextMissing` reason in audit. This is NOT an error — the dispatcher records and continues with whatever recipients did resolve.

#### Scenario: Submitter resolves from context

- **GIVEN** `ctx.SubmitterUserId = u_emp`
- **WHEN** the resolver evaluates `{ type: 'submitter' }`
- **THEN** the result is `Success({ u_emp })`

#### Scenario: Submitter null returns empty (not error)

- **GIVEN** `ctx.SubmitterUserId = null`
- **WHEN** the resolver evaluates `{ type: 'submitter' }`
- **THEN** the result is `Success(empty)` with `RuntimeContextMissing` audit reason

#### Scenario: Actor delegates to IActorResolver

- **WHEN** the resolver evaluates `{ type: 'actor', inner: { type: 'functional_members', function_tag: 'finance' } }`
- **THEN** the call delegates to `IActorResolver` with the inner ref and a context derived from `NotificationContext` (initiator = SubmitterUserId, form_data = Variables)

### Requirement: NotificationContext carries the runtime fields needed to resolve recipients

A `NotificationContext` record SHALL carry, at minimum:

- `FlowInstanceId` (Guid?, nullable until ProcessRuntime ships)
- `SubmitterUserId` (Guid?, nullable when caller did not supply)
- `CurrentApproverUserId` (Guid?)
- `CurrentAssigneeUserId` (Guid?)
- `Variables` (IReadOnlyDictionary<string, JsonElement>) — used both for Mustache template rendering AND for `form_field_ref` ActorRef resolution

The dispatcher's contract: it accepts a NotificationContext from the caller and passes it to the resolver. It does NOT manufacture missing fields; if the caller did not supply `SubmitterUserId`, the resolver returns empty for `submitter` recipient (not an error).

#### Scenario: Context fields propagate to resolver

- **GIVEN** dispatcher receives ctx with all four user IDs set
- **WHEN** the resolver runs against a notification with mixed recipient types
- **THEN** each runtime variant resolves to the corresponding user ID, and `actor` variants get a derived ActorContext with these fields

### Requirement: ResolutionError.Kind extended for notification context

The `ResolutionError.Kind` enum SHALL include `RuntimeContextMissing` for the case where a runtime-scoped recipient (`submitter`, `current_approver`, `current_assignee`) is referenced but the corresponding `NotificationContext` field is null. Audit rows SHALL record which field was missing in the error reason text.

#### Scenario: RuntimeContextMissing reason carries field name

- **WHEN** a notification recipient references `current_approver` and ctx.CurrentApproverUserId is null
- **THEN** the audit row's ErrorReason text includes `"current_approver"` for triage

### Requirement: Recipient set is union across all NotifyRecipientRef entries

When a notification's `recipients[]` array contains multiple entries, the dispatcher SHALL resolve each entry independently and take the union of the resulting user sets. Duplicates (a user appearing in multiple entries) SHALL be deduplicated to one delivery per (user, channel) tuple.

#### Scenario: Duplicate users deduplicated

- **GIVEN** recipients `[{ type: 'submitter' }, { type: 'actor', inner: { type: 'role', code: 'CEO' } }]`
- **AND** the submitter is also assigned the CEO role (rare but possible)
- **WHEN** dispatch runs
- **THEN** exactly one delivery row per channel is inserted for that user (not two)

## ADDED Requirements

### Requirement: NotifyRecipientRef discriminated union

The system SHALL define `NotifyRecipientRef` as a discriminated union over four variants. Every `NotifyRecipientRef` MUST carry a `type` field whose value is one of: `"submitter"`, `"current_approver"`, `"current_assignee"`, `"actor"`. The `actor` variant SHALL wrap an `ActorRef` in its `inner` field. The validator MUST reject any object lacking `type` or carrying a `type` value outside this set.

The first three types are *runtime-scoped* — they resolve in the context of a flow instance against `NotificationContext`, not at spec-load time against the org graph. The `actor` variant resolves via the standard `IActorResolver`.

#### Scenario: Valid submitter recipient

- **WHEN** spec.json contains `{ "type": "submitter" }` in a notification's recipients
- **THEN** the validator accepts it

#### Scenario: Valid actor recipient

- **WHEN** spec.json contains `{ "type": "actor", "inner": { "type": "functional_members", "function_tag": "finance" } }`
- **THEN** the validator accepts it (inner ActorRef validates via `ActorRefValidator`)

#### Scenario: Missing inner on actor type rejected

- **WHEN** spec.json contains `{ "type": "actor" }` with no `inner` field
- **THEN** the validator rejects it

#### Scenario: Unknown recipient type rejected

- **WHEN** spec.json contains `{ "type": "ghost" }`
- **THEN** the validator rejects it listing the allowed type values

### Requirement: Notification.recipients uses NotifyRecipientRef

A `Notification` SHALL carry `recipients: NotifyRecipientRef[]` (non-empty array). The legacy free-form union of `'submitter' | 'current_approver' | 'role:X' | 'user:X'` SHALL be removed from new specs; importers MUST migrate legacy specs:

- `{ type: 'submitter' }` → `{ type: 'submitter' }` (unchanged)
- `{ type: 'current_approver' }` → `{ type: 'current_approver' }` (unchanged)
- `{ type: 'role', code: 'X' }` → `{ type: 'actor', inner: { type: 'role', code: 'X' } }`
- `{ type: 'user', id: 'X' }` → `{ type: 'actor', inner: { type: 'user', id: 'X' } }`
- Any embedded `expr` / `group` / `conditional` / `collection` (rare in legacy) wraps similarly into `actor`

#### Scenario: Notification with mixed recipient types

- **WHEN** a Notification carries `recipients = [{ type: 'submitter' }, { type: 'actor', inner: { type: 'functional_members', function_tag: 'finance' } }]`
- **THEN** the validator accepts both elements

#### Scenario: Empty recipients rejected

- **WHEN** a Notification carries `recipients = []`
- **THEN** the validator rejects with "notification recipients required"

#### Scenario: Legacy role recipient migrated

- **WHEN** a legacy notification carries `recipients = [{ type: 'role', code: 'HR' }]`
- **AND** the importer runs
- **THEN** the migrated recipients are `[{ type: 'actor', inner: { type: 'role', code: 'HR' } }]`

### Requirement: NotifyTemplate variables set must equal body+subject placeholder set

A `NotifyTemplate` SHALL declare `variables: string[]`. The set of placeholders extracted from `subject['zh-TW']` and `body['zh-TW']` (Mustache `{{...}}` tokens, including dotted paths like `{{leave.days}}`) MUST equal the set declared in `variables`. The validator MUST reject any template where the two sets differ (set inequality, not subset).

#### Scenario: Variables match placeholders

- **WHEN** subject = `"Hi {{name}}"`, body = `"Days: {{days}}"`, variables = `["name", "days"]`
- **THEN** the validator accepts it

#### Scenario: Missing variable declaration

- **WHEN** subject = `"Hi {{name}}"` and variables = `[]`
- **THEN** the validator rejects with "variables[] missing: name"

#### Scenario: Extra variable declaration

- **WHEN** subject = `"Hi {{name}}"` and variables = `["name", "balance"]` (balance not in template)
- **THEN** the validator rejects with "variables[] extras: balance — declared but not referenced"

#### Scenario: Wizard auto-detect button rewrites variables

- **WHEN** the user clicks "Auto-detect variables" in StepNotify
- **THEN** the wizard parses subject + body, extracts placeholders, sets `variables[]` to that set; subsequent validation passes

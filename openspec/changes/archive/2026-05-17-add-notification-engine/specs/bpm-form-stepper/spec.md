## ADDED Requirements

### Requirement: StepNotify recipient editor uses NotifyRecipientEditor

The `StepNotify` wizard step SHALL render a `NotifyRecipientEditor` component for each notification's recipient list, replacing the legacy 4-type `RecipientsEditor`. The editor SHALL allow the user to select among:

- 申請人 (`{ type: 'submitter' }`)
- 當前審核者 (`{ type: 'current_approver' }`)
- 當前 assignee (`{ type: 'current_assignee' }`)
- 進階 (`{ type: 'actor', inner: <ActorRef> }`) — opens the standard `ActorRefEditor` for the inner ref

When the user picks "進階", the inner ActorRefEditor SHALL offer all available types from the latest ActorRef vocabulary (including `functional_head`, `functional_members`, `by_amount`, `title_match`, `unresolved` once the prior proposals land).

#### Scenario: Picking submitter is one click

- **WHEN** the user opens the recipient picker and selects 申請人
- **THEN** the recipient is `{ type: 'submitter' }` with no further input required

#### Scenario: Advanced opens ActorRefEditor

- **WHEN** the user selects 進階 and chooses inner type 部門功能成員 with function_tag = "finance"
- **THEN** the recipient is `{ type: 'actor', inner: { type: 'functional_members', function_tag: 'finance' } }`

### Requirement: StepNotify validator enforces template integrity

`validators.notify` SHALL return invalid (with errors) when any of the following hold for any notification:

- `subject['zh-TW']` is empty
- `body['zh-TW']` is empty
- `recipients` is empty
- `channel` is empty
- `trigger` is not in the trigger enum
- The set of `{{...}}` placeholders in `subject + body` is not equal to the set declared in `variables[]`

The validator SHALL aggregate errors across all notifications, returning a clear list (one error per notification × failure mode).

#### Scenario: Empty subject blocked

- **GIVEN** a notification with `subject = { 'zh-TW': '' }`
- **WHEN** the validator runs
- **THEN** the result is invalid with error `"notify_X: subject (zh-TW) required"`

#### Scenario: Mismatched variables list blocked

- **GIVEN** body = `"Hi {{name}}"`, variables = `[]`
- **WHEN** the validator runs
- **THEN** the result is invalid with error `"notify_X: variables[] missing 'name'"`

#### Scenario: Multiple notifications, multiple errors

- **GIVEN** notification 1 has empty body, notification 2 has empty recipients
- **WHEN** the validator runs
- **THEN** the result lists both errors

### Requirement: Auto-detect variables button

The recipient template editor SHALL include an "Auto-detect variables" button. Clicking it SHALL parse `subject['zh-TW']` and `body['zh-TW']` for `{{...}}` tokens (including dotted paths) and rewrite `variables[]` to exactly that set (deduplicated, sorted).

#### Scenario: Auto-detect populates from body

- **GIVEN** body = `"Hi {{name}}, balance: {{balance}}"`, variables = `[]`
- **WHEN** the user clicks "Auto-detect variables"
- **THEN** variables = `["balance", "name"]`

#### Scenario: Auto-detect strips orphans

- **GIVEN** body = `"Hi {{name}}"`, variables = `["name", "ghost"]`
- **WHEN** the user clicks "Auto-detect variables"
- **THEN** variables = `["name"]` (ghost removed because no `{{ghost}}` token in body)

### Requirement: Preview button renders against sample variables

The notification editor SHALL include a "Preview" button per notification. Clicking it SHALL call `POST /api/notifications/dev-fire` with the inline notification spec and a sample context (the wizard auto-generates a sample with each variable filled with `<sample-name>` or seeded from existing form schema where possible). The response SHALL be displayed in a modal showing:

- The rendered subject + body (after Mustache substitution)
- The list of resolved target users (their names)
- The channel breakdown (how many in_app, how many email)
- Any unbound placeholders or resolution failures

#### Scenario: Preview shows rendered output

- **GIVEN** subject = `"Hi {{name}}"`, body = `"Days: {{days}}"`, sample ctx `{ name: "Mary", days: "5" }`
- **WHEN** the user clicks Preview
- **THEN** the dialog shows subject `"Hi Mary"`, body `"Days: 5"`, target list, and "Unbound: 0"

#### Scenario: Preview shows unbound placeholders

- **GIVEN** body = `"Hi {{name}}, ghost: {{ghost}}"`, sample ctx omits `ghost`
- **WHEN** the user clicks Preview
- **THEN** the dialog shows the rendered output with `{{ghost}}` still visible AND a warning row "Unbound placeholders: ghost"

### Requirement: Demo screens preserved

The mock-up flow screens (`bpm-ui/src/screens/forms/*.tsx`, `Home.tsx`, `Search.tsx`, `Report.tsx`, `lib/workflow.ts`) SHALL NOT be modified by this change. The 9 mock-up flows continue to render identically. This change only updates the notification step in the onboarding wizard, the spec layer, the AppLayout Bell, and the new Notifications screen.

#### Scenario: Demo screens unchanged

- **WHEN** the change is applied
- **AND** a reviewer opens any of the 9 mock-up flows
- **THEN** the visuals are byte-identical to the pre-change state

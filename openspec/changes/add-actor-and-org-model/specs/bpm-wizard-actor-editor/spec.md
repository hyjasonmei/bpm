## ADDED Requirements

### Requirement: ActorRefEditor component renders all six types

The wizard SHALL provide a React component `ActorRefEditor` accepting `value: ActorRef` and `onChange: (next: ActorRef) => void`. The component SHALL render a type picker dropdown with six options (with bilingual labels):
- `expr` → "上級主管 / 部門主管 (Org-chart path)"
- `role` → "角色 (Role)"
- `group` → "群組 (Group)"
- `user` → "指定使用者 (User)" — visually marked as test-only
- `conditional` → "條件式 (Conditional)"
- `collection` → "合議 (Collection)"

Selecting a type SHALL replace `value` with a fresh ActorRef of that shape, calling `onChange` with the new value.

#### Scenario: Switching from expr to role
- **WHEN** the editor is rendered with `value = { type: "expr", path: "submitter.manager" }`
- **AND** the user selects "Role" in the type dropdown
- **THEN** `onChange` is called with `{ type: "role", code: "" }` (or a sensible default)
- **AND** the editor re-renders showing the role-code input

### Requirement: expr editor uses constrained path picker

When `value.type = "expr"`, the editor SHALL render a dropdown listing exactly the whitelisted paths (lowercase strings; bilingual display labels). Free-text entry SHALL NOT be permitted.

#### Scenario: All whitelisted paths available
- **WHEN** the expr editor is rendered
- **THEN** the path dropdown contains all whitelisted paths from the actor-dsl spec, no more, no less

#### Scenario: Invalid path cannot be entered
- **WHEN** the user has any UI surface for path
- **THEN** there is no free-text input — only a select control bound to the whitelist

### Requirement: conditional editor recursively renders ActorRefEditor

When `value.type = "conditional"`, the editor SHALL render:
- A condition builder (form-field picker, operator dropdown, value input)
- Nested `<ActorRefEditor>` for `then`
- Nested `<ActorRefEditor>` for `else`

The editor SHALL refuse to render the inner `<ActorRefEditor>` past nesting depth 3 (matching the validator cap), instead displaying "Maximum nesting reached — use a Collection or restructure".

#### Scenario: Three-level nesting allowed
- **WHEN** value is a conditional whose then is a conditional whose then is a conditional (depth 3)
- **THEN** all three levels render normally

#### Scenario: Four-level nesting blocked
- **WHEN** value would be a conditional at nesting depth 4
- **THEN** the inner editor renders a disabled state with the "Maximum nesting reached" message

### Requirement: collection editor lists actors with min_approvals input

When `value.type = "collection"`, the editor SHALL render:
- A mode toggle (`any` / `all`)
- A `min_approvals` numeric input (visible only when `mode = "any"`)
- A list of `<ActorRefEditor>` for each entry in `actors`, with add/remove controls

The `min_approvals` input SHALL be capped at `actors.length`; the editor SHALL surface a warning if the user tries to set higher.

#### Scenario: Adding an actor extends the list
- **WHEN** the user clicks "Add actor"
- **THEN** a new entry is appended (defaulting to `{ type: "expr", path: "submitter.manager" }` or similar) and `onChange` is called

#### Scenario: min_approvals warning
- **WHEN** the user types a `min_approvals` value greater than `actors.length`
- **THEN** the input shows a warning "must be ≤ <actors length>" and the value is clamped on blur

### Requirement: StepApprovers / StepDecisions / StepNotify use ActorRefEditor

The wizard steps `StepApprovers`, `StepDecisions`, `StepNotify` SHALL replace any prior plain-text "approver" or "assignee" field with an `<ActorRefEditor>`. The persisted spec.json values for those fields SHALL be `ActorRef` objects of the typed-discriminator form, never strings.

#### Scenario: StepApprovers persists ActorRef object
- **WHEN** the user edits the approver via the wizard, then submits
- **THEN** the produced spec.json contains `approver: { type: "...", ... }` for the relevant step, not `approver: "<some string>"`

### Requirement: RoleSwitcher calls dev-login endpoint

The top-bar `RoleSwitcher` (introduced in `add-bpm-frontend` as a localStorage-backed flag) SHALL be rewired to call `POST /api/dev/login { persona_code }` on selection, store the returned JWT in `localStorage.bpm_jwt`, and update any in-memory user state accordingly.

#### Scenario: Persona switch issues new JWT
- **WHEN** the user picks "Manager"
- **THEN** the frontend POSTs to /api/dev/login, receives a JWT, stores it in localStorage, and subsequent fetches include the new bearer

#### Scenario: Persona switch failure surfaces error
- **WHEN** the dev-login POST returns non-200
- **THEN** the RoleSwitcher displays an error toast and does NOT clear the previous token

## ADDED Requirements

### Requirement: StepForms exposes assignee picker per userTask

The `StepForms` wizard step SHALL render, for every userTask node in the flow, a "誰來填 (Assignee)" panel above the field editor. The panel SHALL host an `ActorRefEditor` instance bound to `draft.userTasks[].assignee`. Changing the assignee MUST persist into the draft and be reflected in the exported spec.json.

#### Scenario: New userTask defaults to expr:submitter

- **WHEN** a new userTask node is added to the flow
- **AND** `StepForms` mounts the userTask card
- **THEN** the assignee panel shows `ActorRef = { type: 'expr', path: 'submitter', skip_if_initiator: false }` as default

#### Scenario: User picks functional_members

- **WHEN** the user selects "部門功能成員" from the type dropdown
- **AND** picks `function_tag = "hr"`
- **THEN** the draft userTask's assignee is `{ type: 'functional_members', function_tag: 'hr' }`

#### Scenario: Assignee persists to exported spec.json

- **WHEN** the user exports the draft via the Export button
- **THEN** every userTask in the JSON carries `assignee` (not `permissions`)

### Requirement: StepForms exposes viewers picker per userTask

The userTask card SHALL host a "誰可看 (Viewers)" section below the field editor. The section SHALL allow the user to select any combination of:

- `self` — the user who initiated the flow
- `submitter` — alias of self (offered for clarity in notification contexts)
- `current_assignee` — whoever currently holds an open task in this flow
- one or more `ActorRef` entries (role / group / function_tag / expr / collection)

The section SHALL default to `[self, current_assignee]` when the userTask is first created.

#### Scenario: Viewer multi-select

- **WHEN** the user toggles `self` and `current_assignee` chips
- **AND** clicks "add viewer" → picks `functional_members:hr`
- **THEN** `userTask.viewers = [{ type: 'self' }, { type: 'current_assignee' }, { type: 'actor', inner: { type: 'functional_members', function_tag: 'hr' } }]`

#### Scenario: Default viewers when omitted

- **WHEN** a new userTask is created without explicit viewer selection
- **THEN** `viewers = [{ type: 'self' }, { type: 'current_assignee' }]`

### Requirement: Demo screens preserved

The demo flow screens (`bpm-ui/src/screens/Home.tsx`, `bpm-ui/src/screens/forms/*.tsx`, `bpm-ui/src/screens/Search.tsx`, `bpm-ui/src/screens/Report.tsx`, `bpm-ui/src/lib/workflow.ts`) SHALL NOT be modified by this change. The 9 mock-up flow visuals (LEAVE / GEE / GEV / APE / TRQ / TEO / HWP / ITPR / EXTOB) MUST continue to render with the existing persona-aware step ribbon and form components.

#### Scenario: Demo screens unchanged

- **WHEN** the change is applied
- **AND** a reviewer opens `Home`, picks a persona, and walks each form
- **THEN** the visuals are byte-identical to the pre-change state

### Requirement: ActorRefEditor type picker includes functional_members

The `ActorRefEditor` component (used by both `StepApprovers` and the new `StepForms` assignee panel) SHALL include `functional_members` as a selectable type with the label "部門功能成員". Selecting it SHALL show a child editor with: function_tag dropdown (drawn from `FunctionTagWhitelist`), `include_subtree` toggle, `active_only` toggle (defaults checked).

#### Scenario: Picker shows functional_members option

- **WHEN** the user opens the ActorRefEditor type picker
- **THEN** the dropdown includes "部門功能成員" alongside the existing 8 options (after the previous proposal lands: 部門功能主管 / 金額簽核 / 職稱比對 / 待釐清)

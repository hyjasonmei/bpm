# bpm-shell-ui (delta) — SPA routing

## ADDED Requirements

### Requirement: URL reflects current screen

The bpm-ui SHALL use `react-router-dom` to bind every primary screen to
a URL. The browser URL SHALL change as the user navigates, and a
deep-link MUST open directly to the named screen on reload.

#### Scenario: Task deep-link

- **GIVEN** a user has a JWT for the task's assignee
- **WHEN** they paste `/tasks/<taskId>` into the address bar
- **THEN** the corresponding form SHALL open in task mode
- **AND** no intermediate Home render SHALL be required

#### Scenario: Case deep-link

- **WHEN** the user opens `/cases/<instanceId>` directly
- **THEN** `<CaseDetail>` SHALL render with that instance's data

#### Scenario: Form create deep-link

- **WHEN** the user opens `/apply/<CODE>` directly
- **THEN** the form for `<CODE>` SHALL render in create mode

### Requirement: Browser navigation works

#### Scenario: Back / forward

- **WHEN** the user clicks through Home → task form → case detail
- **AND** then presses the browser back button
- **THEN** they SHALL return to the task form
- **AND** the URL SHALL match the previous screen

### Requirement: Form-component prop contract preserved

The per-flow form components SHALL continue to receive the same props
they receive today (`persona`, `mode`, `taskId`, `onSubmitted`).
Chef-cooked forms MUST NOT need edits for the router migration.

## REMOVED Requirements

### Requirement: `Screen` union as navigation source of truth

**Reason**: Replaced by react-router. The `Screen` discriminated union
in `AppLayout.tsx` and its persistence to `localStorage` under
`bpm_screen` are removed; URL is the source of truth.

**Migration**: Existing callers that called `setScreen({…})` switch to
`useNavigate()`. `App.tsx`'s `readSavedScreen` / `SCREEN_KEY` logic is
deleted (the router restores location via the browser history API).

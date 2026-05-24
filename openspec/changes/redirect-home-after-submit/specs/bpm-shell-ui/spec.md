# bpm-shell-ui (delta) — Post-submit redirect + Home refresh

## ADDED Requirements

### Requirement: Form submit navigates to a useful destination

`useFormRuntime` SHALL navigate the user after any successful runtime
mutation. The default destinations are:

- After Create-mode `submitCreate`: `/tasks/:firstTaskId` when the
  first spawned task is assigned to the current user, otherwise `/`.
- After Task-mode `submitUserTask`, `approve`, `reject`, or `returnTask`:
  `/`.

Forms MAY override the destination by passing an `onSubmitted` callback
in the `FormRuntimeProps`; when present, the hook calls it instead of
performing the default navigation.

#### Scenario: Employee submits a leave request

- **GIVEN** the LEAVE V1 spec's first task is assigned to the
  employee's manager (not the employee)
- **WHEN** the employee submits the form successfully
- **THEN** the URL SHALL change to `/`
- **AND** the new instance SHALL be visible in Home's recent-cases /
  inbox without manual refresh and without waiting for the 30 s poll

#### Scenario: Manager approves a task

- **GIVEN** a manager is viewing `/tasks/:id` for a leave they own
- **WHEN** they click Approve and the runtime returns success
- **THEN** the URL SHALL change to `/`
- **AND** the just-approved task SHALL no longer appear in their inbox

### Requirement: Tasks-invalidate event keeps Home fresh

`useFormRuntime` SHALL dispatch a `CustomEvent('bpm:tasks-invalidate')`
on `window` after every successful runtime mutation. The hooks
`useMyTasks` and `useMyInstances` SHALL listen for this event and
re-fetch immediately, in addition to their existing 30 s poll.

#### Scenario: Same-tab refresh

- **WHEN** any form-runtime mutation succeeds
- **THEN** `bpm:tasks-invalidate` SHALL be dispatched
- **AND** any mounted `useMyTasks` / `useMyInstances` SHALL re-fetch
  within 250 ms

#### Scenario: Coalescing

- **WHEN** N events fire within 250 ms (e.g. a batch operation)
- **THEN** only one refresh SHALL run

## MODIFIED Requirements

### Requirement: `useFormRuntime` API

The hook signature SHALL gain access to a navigation function (via
`useNavigate()` from the SPA router introduced by `add-spa-routing`)
so it can perform the default redirect itself. Callers continue to
import the same hook with the same prop names; behaviour changes
without a signature change.

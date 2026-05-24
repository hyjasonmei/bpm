# bpm-shell-ui (delta) — Account menu + logout

## ADDED Requirements

### Requirement: Account menu in AppLayout top-right

The bpm-ui AppLayout SHALL render an `<AccountMenu>` in its top-right
slot, replacing the standalone `<RoleSwitcher>`.

#### Scenario: Authenticated user can see who they are

- **WHEN** the user has a valid JWT
- **THEN** the AccountMenu button SHALL show the user's avatar (initials)
  and (on `md`+ viewports) their full name
- **WHEN** the menu is opened
- **THEN** it SHALL display the user's full name, email, and current
  role badge, all sourced from JWT claims (`name`, `email`, `roles[]`)

#### Scenario: Logout returns to Login

- **WHEN** the user clicks "Logout" in the menu
- **THEN** the stored JWT SHALL be cleared
- **AND** the `bpm:auth-cleared` event SHALL be dispatched
- **AND** the `<Login>` screen SHALL render on the next tick

### Requirement: Dev-mode role switch is conditional

The role switch submenu SHALL appear in AccountMenu **only** when the
`/api/dev/login` endpoint is reachable (i.e. `BPM_AUTH_MODE=dev` on the
server). In `prod` mode it SHALL be hidden.

#### Scenario: Dev mode

- **WHEN** the server has `BPM_AUTH_MODE=dev`
- **THEN** the AccountMenu SHALL render a "Switch role" submenu listing
  the six personas
- **AND** picking a persona SHALL behave the same as the legacy
  `<RoleSwitcher>` (mint a new persona JWT via `/api/dev/login` and
  reload)

#### Scenario: Prod mode

- **WHEN** the server has `BPM_AUTH_MODE=prod`
- **THEN** the AccountMenu SHALL NOT render the role-switch submenu
- **AND** identity + logout SHALL still be available

### Requirement: RoleSwitcher retired from AppLayout slot

The standalone `<RoleSwitcher>` SHALL no longer be rendered directly by
`AppLayout`. The component file MAY remain for internal reuse inside
`AccountMenu`, but external composition points (top-right slot) MUST go
through `AccountMenu`.

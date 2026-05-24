## Why

Jason reported 2026-05-24 that the top-right of bpm-ui has a
`RoleSwitcher` but no actual logout button and no clear "who am I"
indicator. Switching roles works, but a real user can't tell which
account they're logged in as, can't see their email, and can't sign out
— all blockers for the dogfood-with-partner phase.

## What Changes

### NEW `components/AccountMenu.tsx`

Replaces `RoleSwitcher` in the AppLayout top-right slot. Single button
that opens a popover with:

- **Identity block** — avatar (initials), full name, email, current role
  badge. Sourced from the JWT (`sub`, `name`, `email`, `roles[]`) via
  the existing `useActivePersona` hook (which already exposes
  `authedFullName` etc.).
- **Switch role submenu** — only visible when `BPM_AUTH_MODE=dev` (i.e.
  the `/api/dev/login` endpoint is registered). Same persona list
  RoleSwitcher renders today. In `prod` mode the submenu is hidden
  entirely.
- **Sandbox persona switch** — when sandbox is on, the existing sandbox
  persona-impersonation flow folds into this same menu (one place to
  switch identity).
- **Logout** — clears the JWT (`clearJwt()`), dispatches
  `bpm:auth-cleared`, and `App.tsx`'s existing `AuthGate` falls back to
  the Login screen.

### How the dev-mode role switch keeps working

`AccountMenu` consumes the same `useActivePersona` + `onChange` hooks
RoleSwitcher does today, so the wizard's zero-click dev-mode flow is
unchanged. The dev-mode badge ("DEV — Switch role") is visible only
when the dev-login endpoint is reachable.

### Mobile / narrow-viewport

Below `md`, the button collapses to avatar-only; the popover full-bleeds
to the right edge.

### Out of scope

- Profile editing (name / avatar upload)
- Password change UI (admin handles this)
- 2FA / MFA flows
- Active sessions list
- "Switch tenant" (single-tenant POC)

## Capabilities

### New

- `bpm-shell-ui` (UI core capability for bpm-ui shell components) —
  adds the `AccountMenu` component contract.

### Modified

- `bpm-shell-ui` (same capability, if already added by another change)
  — `RoleSwitcher` retired from the AppLayout slot; kept as a
  lower-level building block AccountMenu consumes internally.

## Impact

- `bpm-ui/src/components/AccountMenu.tsx` — new
- `bpm-ui/src/components/AppLayout.tsx` — swap `<RoleSwitcher>` for
  `<AccountMenu>` in the top-right slot
- `bpm-ui/src/components/RoleSwitcher.tsx` — kept (internal to
  AccountMenu, no longer rendered standalone)
- `bpm-ui/src/lib/apiFetch.ts` — no change (`clearJwt` already exists)
- No backend changes
- No DB migration
- No chef-skill update (chef doesn't touch shell components)

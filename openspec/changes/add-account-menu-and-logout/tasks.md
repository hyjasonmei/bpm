# Tasks

## 1. AccountMenu component

- [ ] 1.1 Create `AccountMenu.tsx` with identity block + role submenu + logout
- [ ] 1.2 Source identity from `useActivePersona()` + JWT claims (`name`, `email`)
- [ ] 1.3 Dev-mode detection: hide role switch when `/api/dev/login` is not registered (probe via 404 once on mount, cache the result)
- [ ] 1.4 Click-outside + Escape close behaviour matching existing popovers (`HelpReportMenu`, `NotificationsMenu`)

## 2. Logout wiring

- [ ] 2.1 Logout button calls `clearJwt()` + dispatches `bpm:auth-cleared`
- [ ] 2.2 Verify `App.tsx` AuthGate falls back to `<Login>` immediately
- [ ] 2.3 On Login screen, no stale localStorage keys leak (`bpm_screen`, `bpm_jwt_pre_impersonation`)

## 3. AppLayout swap

- [ ] 3.1 Replace `<RoleSwitcher>` import + usage with `<AccountMenu>`
- [ ] 3.2 Keep `RoleSwitcher.tsx` file (used internally by AccountMenu)
- [ ] 3.3 Verify wizard's zero-click dev flow still works end-to-end

## 4. Verify

- [ ] 4.1 `npx tsc -p tsconfig.app.json --noEmit` clean
- [ ] 4.2 Manual: dev mode → menu shows role switch; logout returns to Login
- [ ] 4.3 Manual: prod mode (env override) → menu hides role switch; still has identity + logout
- [ ] 4.4 chrome-devtools screenshot of open menu (light + dark of identity block) on PR

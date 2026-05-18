# Tasks

## 1. Nav restructure

- [x] 1.1 Define top-level `AppShell` with five-page nav (AI Kitchen / User & Role / Sandbox / Audit / Site Setting) — `src/flowcook/app/AppShell.tsx` + `Nav.tsx`
- [x] 1.2 Each page is a routed React component with placeholder content (state-driven page switch; React Router not added in this minimal skeleton)
- [x] 1.3 Move legacy pages behind a `LEGACY_ADMIN_UI_VISIBLE` flag (default off) — `App.tsx` routes through `FlowcookRoot` first; legacy tree only loads when localStorage `flowcook_legacy_visible=1`

## 2. Login + session

- [x] 2.1 Login page with username / password form — `src/flowcook/auth/LoginPage.tsx`
- [x] 2.2 Wire POST to `bpm-admin-svc /api/auth/login` via Vite proxy
- [x] 2.3 Cookie-based session; redirect to AppShell on success — `useAuth` calls `/api/auth/me` on boot and after login
- [x] 2.4 401 handler — `useAuth` treats `/api/auth/me` 401 as `unauthenticated`, falls back to LoginPage
- [x] 2.5 Logout in user menu — `Nav` bottom panel "Sign out" button calls `/api/auth/logout`

## 3. User & Role page (first real page)

- [x] 3.1 Principal list table with filter by `type` (user / dept / group) — `UserRolePage`
- [x] 3.2 Principal detail panel (id / email / active / created / effective roles / delegations)
- [x] 3.3 Create / soft-delete principals (basic create-via-prompt + delete-with-confirm; richer modal UI is incremental)
- [ ] 3.4 UserDept / DeptParent / GroupMember editors as sub-tabs — **deferred** (read-only relationships visible via list filter + selected dept's metadata; full editing UI tracked as follow-up)
- [ ] 3.5 Role list + assignment UI with inherit checkbox — **deferred** (effective roles shown read-only on user detail; full assignment UI tracked as follow-up)
- [ ] 3.6 Delegation list + create / cancel — **partial** (read-only list on user detail done; create/cancel UI tracked as follow-up)

## 4. Sandbox / Audit / Site Setting placeholder pages

- [x] 4.1 Sandbox placeholder with "Coming in Step 4-6" hint
- [x] 4.2 Audit placeholder with "Coming in Step 6" hint
- [x] 4.3 Site Setting placeholder

## 5. Legacy Process Admin Console deprecation

- [x] 5.1 Deprecation banner shown at top of legacy app body
- [x] 5.2 Legacy tree only loaded when `LEGACY_ADMIN_UI_VISIBLE` flag is on (default off)

## 6. Tests / verification

- [x] 6.1 Manual: log in (Alice cookie session), navigate five pages, create / delete principal — verified via chrome-devtools
- [x] 6.2 Snapshot the new nav structure — saved to `.docs/flowcook-doc/step2-userrole-alice.png` and `step2-ai-kitchen-placeholder.png`
- [x] 6.3 Confirm legacy pages no longer in primary nav — confirmed; legacy tree hidden unless flag explicitly set

## Follow-ups (move to later changes when scheduled)

- 3.4 UserDept / DeptParent / GroupMember editors as sub-tabs (full CRUD UI for memberships)
- 3.5 Role list + assignment UI with `inherit_to_members` checkbox (write side)
- 3.6 Delegation create / cancel UI (write side)
- Polished modals replacing `window.prompt` for principal create
- "Persona switch" UI for admins on the persona-switch allow list (Site Setting integration)

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
- [x] 3.4 UserDept / DeptParent / GroupMember editors — landed as per-type editor sections on `PrincipalDetail` (Dept memberships with primary-star toggle for users; Parent dept picker for depts; Members editor accepting user/dept/group for groups)
- [x] 3.5 Role list + assignment UI with inherit checkbox — Roles tab provides role CRUD (system roles read-only); PrincipalDetail "Role assignments" section provides Assign / Revoke / inheritToMembers pill
- [x] 3.6 Delegation list + create / cancel — inline "New delegation" form (target picker + datetime window + reason) and per-row Cancel on PrincipalDetail

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

## 7. Brand alignment (added after initial draft)

- [x] 7.1 Drop the "flowcook editorial kitchen craft" palette (parchment / saffron / espresso / Fraunces) and adopt `bpm-ui` shared tokens (slate / blue / amber + DM Sans). Rationale captured in `feedback_admin_ui_brand_align.md` — admin must read as one product with bpm.
- [x] 7.2 Logo = ChefHat icon inside the `bg-red-500` square that bpm-ui uses (consistent brand mark, AI-Kitchen-flavoured icon).
- [x] 7.3 Picker dropdowns positioned by `align?: 'left' | 'right'` so they don't get clipped by the detail panel's `overflow-auto` body.

## Follow-ups (move to later changes when scheduled)

- Polished modals replacing `window.prompt` for principal create
- "Persona switch" UI for admins on the persona-switch allow list (Site Setting integration)
- AppShell page hint binding to sub-tab state (currently always shows the sidebar nav hint, e.g. "PRINCIPALS" while on the Roles tab)
- `GET /api/roles/{id}/usage` — replace RoleEditor's N+1 probe that lists every principal's roles to count usage
- `DbPathResolver` for `bpm-admin-svc` so SeedCli and Api stop reading two separate `admin.dev.db` files keyed off CWD

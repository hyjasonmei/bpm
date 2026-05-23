# bpm-dev-cleanup-employee-app

## Why

The chef MVP demo (LEAVE V1 on `leave-test-1`) showed the whole loop
works end-to-end — wizard → bundle → chef → working bpm-svc + bpm-ui
code → /apply/LEAVE submits → ProcessRuntime accepts → audit row lands.

But on the **bpm employee app side** there's still a lot of demo
scaffolding from before the pivot:

- `App.tsx` ships imports for all 11 `Reference_*Form.tsx` and the
  legacy `case 'LEAVE': <LeaveForm />` switch, gated by
  `VITE_SHOW_LEGACY`. Dead weight when chef now owns flow components.
- Home's Quick Actions list is hardcoded to 8 entries from
  `lib/workflow.ts` `FORMS` map. They render regardless of whether
  chef has shipped a manifest for any of them. If a customer logged
  in today, they'd see 8 buttons leading to "not cooked yet"
  placeholders.
- The `RoleSwitcher` in the top right is labelled "Switch demo
  persona" and currently mints persona JWTs via `/api/dev/login`.
  Jason wants this to stay as a real product feature
  (impersonation) — but it has to go through a real impersonation
  API, not the dev mock.
- Mock data in `lib/mocks.ts` (`MOCK_CASES` / `MOCK_ACTIVITY` /
  `MOCK_REMINDERS`) is still imported by `Report.tsx`; `MOCK_ACTIVITY`
  is dead since Phase 2.2 but the file lingers.
- Various UI strings carry `demo` / `示範` / `[demo]` markers that
  read like a half-finished product.

End-state Jason wants: **a customer logs into bpm-ui and sees one
real flow (chef's LEAVE V1, once `leave-test-1` is rebased onto a
post-merge main) with zero "demo" word in the system**.

## What Changes

### `bpm-ui/src/App.tsx` — registry-only dispatch

- Drop the 11 `Reference_*Form` imports + the `case 'LEAVE': ...`
  switch + the `VITE_SHOW_LEGACY` gating.
- Single dispatch path: `lookupForm(screen.code)` → render component
  if found, else `<NotCookedYet code={screen.code} />`.
- `Reference_*Form.tsx` files stay on disk untouched — chef still
  reads them as visual reference (per `chef/skill/SKILL.md` §1).

### `bpm-ui/src/screens/Home.tsx` — Quick Actions from registry

- `QUICK_ACTIONS` hardcoded array deleted.
- `QuickActionsPanel` reads `formRegistry` (or a `useFormRegistry()`
  hook if we add one), shows one button per registered manifest.
- Empty registry → empty Quick Actions card with a "no flows
  available yet — talk to your admin" empty state.
- Adding a manifest under `bpm-ui/src/features/<CODE>/V<N>/` (chef
  output) automatically surfaces the button after Vite reload.

### `bpm-ui/src/lib/workflow.ts` FORMS map — retain as display metadata only

- Don't trim the map — `FORMS[code]` is still useful for the
  Chinese display name / icon / step labels that show up in inbox
  rows + activity feed.
- But adding a key here MUST NOT make a flow appear in Quick Actions
  / dispatch. Those go through the registry.
- Add a header comment documenting the boundary: "FORMS is
  metadata, not a gate. The registry decides what's available."

### `RoleSwitcher` — real impersonation, not dev login

- Today: POSTs `/api/dev/login` (bpm-svc) with `personaCode` to mint
  a fresh JWT. Devtime-only.
- New: POSTs `/api/impersonation/start` (bpm-svc's existing
  `ImpersonationService`, see PR-H1) with `targetUserId`. Returns a
  swapped session token whose `act` claim carries the original user
  + the impersonated target.
- Display name in header still shows current persona, but with a
  small "👁 acting as" marker when impersonation is active.
- The persona dropdown reads from real `Admin_Principals` (admin
  org) instead of the hardcoded persona list. Filter: only show
  users the caller is allowed to impersonate (admin role only, in
  MVP).
- `lib/role.ts` `useActivePersona` refactor: keep the API shape but
  resolve via the impersonation token instead of `/api/dev/login`'s
  persona claim.

### Demo-text scrub

- `RoleSwitcher` aria-label "Switch demo persona" → "Switch user
  (impersonation)".
- Any other `demo` / `[demo]` / `示範` strings flagged by
  `grep -rn -i 'demo' bpm-ui/src` that ship in production paths.
- `lib/mocks.ts` `MOCK_ACTIVITY` and `MOCK_REMINDERS` deleted (dead
  since Phase 2.2). `MOCK_CASES` retained until `Report.tsx` is
  rewritten under `add-real-reporting` — keep the file but trim.

### `Report.tsx` — flag as out-of-scope

- `Report.tsx` is still demo-guarded per `bpm-ui/CLAUDE.md` and
  imports `MOCK_CASES`. Not in bpm-dev scope (`add-real-reporting`
  proposal owns it). Just add a banner to the page making the
  placeholder state explicit, and keep `MOCK_CASES` around for it.

## Out of Scope

- Anything chef-generated. `bpm-dev` is core bpm cleanup; no
  `Features/<CODE>/V<N>/` files land here.
- Anything `bpm-admin-ui` / `bpm-admin-svc`. Admin tooling is
  separate.
- Notification engine real delivery (`add-notification-engine`).
  The audit table is already wired; real-send remains stubbed.
- Real reporting (`add-real-reporting`).
- File storage (`add-file-storage`).

## Design Notes

- **Branch shape**: `bpm-dev` cuts off `main` (post `168283a` skill
  cherry-pick). Merge back to `main` when done. `leave-test-1`
  rebases onto the updated `main` later to give us the "1 real
  flow" demo state.
- **Verification without chef**: bpm-dev's diff lands on `main`
  before any chef-generated manifest is on `main`. To verify the
  registry-driven UI works, drop a stub manifest under
  `bpm-ui/src/features/STUB/V1/manifest.ts` locally, eyeball the UI,
  then delete the stub before commit. Don't depend on `leave-test-1`
  being rebased to test bpm-dev.
- **Why not delete `Reference_*`**: chef's skill explicitly cites
  them as visual reference for layout / tone / repeater patterns.
  Deleting them would hobble chef's quality on subsequent cooks.
- **Why keep `FORMS` map**: inbox rows / activity feed entries
  store a `code` string and need a way to look up the Chinese
  label for display. Doing that lookup via the manifest's component
  is ugly. The map is the right home for static metadata.
- **Why real impersonation now, not later**: Jason explicitly called
  out that the switcher is "an important system feature" and should
  not just swap state client-side. `ImpersonationService` already
  exists in `bpm-svc` (PR-H1) with an audit trail; wiring it in is
  hours of work, not days.

## References

- `openspec/specs/flowcook-architecture/spec.md` — 4-service model
- `chef/skill/SKILL.md` — what chef writes / why bpm-ui must
  registry-dispatch
- `bpm-ui/CLAUDE.md` — current state of the employee SPA after
  PR-L1..L6
- `openspec/changes/archive/2026-05-22-flowcook-mvp-chef-bootstrap/` —
  the MVP chef bootstrap that enabled this cleanup
- Commits this proposal builds on (all on `main`):
  - `8b65e8b` Phase 1.1 db merge
  - `68ee958` Phase 1.2 feature manifest registry + Phase 1.3
    HrFlowsController retired
  - `3f48006` Phase 2.1 NotificationDispatchAudits
  - `41a27ba` Phase 2.2 Home ActivityFeed real data
  - `f87e5d2` admin auto-seed

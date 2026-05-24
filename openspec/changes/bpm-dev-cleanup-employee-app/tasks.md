# Tasks

## 1. App.tsx registry-only dispatch

- [ ] 1.1 Drop the 11 `Reference_*Form` imports + the legacy switch
       case under `if (SHOW_LEGACY_FORMS)` from `bpm-ui/src/App.tsx`
- [ ] 1.2 Drop `VITE_SHOW_LEGACY` env-var reading; remove from
       `.env.example` / `.env.local` if set
- [ ] 1.3 Verify `tsc -p tsconfig.app.json --noEmit` clean —
       Reference_*Form files still compile in isolation (they're
       chef's visual reference)
- [ ] 1.4 Confirm dispatch path is single-track: `lookupForm(code)`
       → component | `<NotCookedYet />` placeholder

## 2. Home Quick Actions from registry

- [ ] 2.1 Delete the hardcoded `QUICK_ACTIONS` array in
       `Home.tsx`
- [ ] 2.2 `QuickActionsPanel` consumes `formRegistry` (export an
       iterable or add a `useFormRegistry()` hook in
       `bpm-ui/src/features/registry.ts`); one button per
       manifest, ordered by `code`
- [ ] 2.3 Quick Actions card shows an empty-state when registry
       is empty: "No flows available yet — talk to your admin"
- [ ] 2.4 Quick Action label resolution: lookup
       `FORMS[manifest.code]?.zhLabel` for display; fallback to
       manifest.code if FORMS map doesn't have the code yet
- [ ] 2.5 Verify with a stub manifest at
       `bpm-ui/src/features/STUB/V1/manifest.ts` (delete before
       commit)

## 3. workflow.ts FORMS map header comment

- [ ] 3.1 Add a top-of-file comment to `bpm-ui/src/lib/workflow.ts`
       explaining FORMS is display-metadata, not a registry gate;
       adding a key here does NOT make the flow appear
- [ ] 3.2 Leave the 11 entries in place — inbox / activity feed
       still look them up by code

## 4. RoleSwitcher → real impersonation

- [ ] 4.1 New bpm-svc endpoints:
       `POST /api/impersonation/start` (body: targetUserId)
       returns a fresh JWT with `act` claim, and
       `POST /api/impersonation/stop` to revert to the original
       user. Existing `ImpersonationService` (PR-H1) carries the
       logic; this exposes it.
- [ ] 4.2 RoleSwitcher dropdown reads `GET /api/principals?type=user`
       (admin-only) for the candidate list, instead of the
       hardcoded persona array
- [ ] 4.3 Click target user → POST to
       `/api/impersonation/start` → store the returned token →
       refresh UI to reflect new identity
- [ ] 4.4 Header shows the impersonated user; when active, add a
       small "👁 acting as <orig user>" marker so the operator
       knows they're not their own account
- [ ] 4.5 `useActivePersona` refactor: drop the persona-code
       enum (admin / manager / employee etc); resolve user from
       JWT claims; persona-based UI gates (`persona === 'hr'`)
       become role-based (`hasRole('hr_reviewer')`) via
       `RoleAssignment` lookup
- [ ] 4.6 Rename aria-label / button text from "Switch demo
       persona" → "Switch user (impersonation)"
- [ ] 4.7 Keep `/api/dev/login` available for local dev (gated
       on `BPM_AUTH_MODE=dev`); it's the bootstrap to get any
       login at all in a fresh checkout. Don't try to delete it
       in this change.

## 5. Demo-text scrub

- [ ] 5.1 `grep -rn -i 'demo' bpm-ui/src` — list everything;
       scrub user-facing strings (aria-labels / button text /
       page copy). Internal comments + dev-only env paths can
       stay.
- [ ] 5.2 Same sweep for `示範` and `[demo]`.
- [ ] 5.3 Delete `MOCK_ACTIVITY` and `MOCK_REMINDERS` exports
       from `lib/mocks.ts` (dead since Phase 2.2). Keep
       `MOCK_CASES` for `Report.tsx`.

## 6. Verify

- [ ] 6.1 `tsc -p tsconfig.app.json --noEmit` clean
- [ ] 6.2 Boot bpm-svc + bpm-ui on a fresh `db/bpm.db`; login as
       `wilson@acme.test`. Home renders with 0 Quick Actions,
       0 cases, 0 pending; "No flows available" message shows.
- [ ] 6.3 Drop a stub manifest at `features/STUB/V1/manifest.ts`,
       restart vite. Quick Actions shows 1 button "STUB". Click
       → component renders or NotCookedYet shows for a missing
       component. Delete stub.
- [ ] 6.4 Persona switcher dropdown lists real users from
       `Admin_Principals` (not the hardcoded six). Click a user →
       header shows new identity with "acting as wilson" marker.
       Stop impersonation → revert to wilson.
- [ ] 6.5 No `demo` / `[demo]` / `示範` strings render in the UI.

## 7. Merge + handoff

- [ ] 7.1 PR `bpm-dev` → `main` via GitKraken; ultrareview
       optional
- [ ] 7.2 After merge, document in
       `docs/MVP_DEMO_RUNBOOK.md` the new "0 flows by default"
       baseline and the impersonation flow
- [ ] 7.3 Rebase `leave-test-1` onto updated `main`; verify
       chef's LEAVE V1 manifest surfaces as the one Quick Action
       in the demo
- [ ] 7.4 Delete `bpm-dev` branch

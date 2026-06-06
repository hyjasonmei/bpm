---
name: bpm-smoke-test
description: Run the flowcook BPM smoke suite (happy + unhappy paths + the demo Reset feature) against the running bpm-svc/admin-svc, and the manual browser checks. Use after backend/identity/flow changes, before a demo, or to reproduce a reported bug.
---

# flowcook BPM smoke test

A repeatable smoke suite for the flowcook stack. The bulk is an API script
(`smoke.sh`) that exercises every shipped flow and the guard rails; a short
manual browser checklist covers the bits only the UI can show.

**Version-agnostic.** The script never hardcodes a flow version. It resolves
each flow at whatever version the bpm launcher currently serves — the highest
**Published** version per flow code in `/api/flow-registry` (the same
latest-per-code + Published gate the employee launcher uses) — and hits
`/api/<flow>/v<live>`. Ship + publish a v2 and the suite follows it onto v2
automatically; the Reset section re-registers at the deployed runtime version
(from `/api/flow-codes`, which now reports the highest `<CODE>_V<N>_Case` per
code) so a reset never silently downgrades the registry to v1. The run prints
the resolved versions in section A.

## When to use

- After touching identity / roles / seeding / flow state machines / the Reset feature.
- Before a demo or UAT handoff (confirms all 10 flows run end-to-end from a clean seed).
- To reproduce or rule out a reported bug — the suite is broad enough to localise most regressions to a section.

## Prerequisites

Both backends must be running on their default ports:

- `bpm-svc` → http://localhost:5290  (`cd bpm-svc/src/Api && dotnet run`)
- `bpm-admin-svc` → http://localhost:5266 (`cd bpm-admin-svc/src/Bpm.Admin.Api && dotnet run --launch-profile http`)

Auth runs in dev mode (`/api/dev/login` mints persona JWTs). Personas:
`employee=Bob, manager=Alice, finance=Frank, hr=Henry, admin=Jack`.

## Run the API suite

```bash
bash .claude/skills/bpm-smoke-test/smoke.sh
# options:
SKIP_RESET=1 bash .claude/skills/bpm-smoke-test/smoke.sh   # skip the destructive Reset section
BPM=http://host:5290 ADM=http://host:5266 bash .claude/skills/bpm-smoke-test/smoke.sh
```

Output is one `PASS`/`FAIL` line per check; exit code = number of failures
(0 = all green). The final RESET section wipes the DB back to seed-init and
leaves a clean slate, so it is safe to leave the demo in a known state.

### What the script covers

- **A. Health + identity** — services up, JWT carries SCREAMING_SNAKE role Codes (not Names), no empty role codes, 10 flows Published.
- **B. Happy paths** — LEAVE short (manager → HR_MANAGER via dept inheritance → Completed), LEAVE long (≥7d → VP step), TEO (manager → FINANCE → Completed), VENDOR_EXPENSE (supervisor → PROCUREMENT), PURCHASE_REQUEST (dept-head → FINANCE), and the six manager-only flows' submit endpoints (APE/EOB/ETM/FAD/FAP/TRQ).
- **C. Unhappy paths** — validation 400 (date order, empty required, empty line-items), authN 401 (missing/bad token), 404 (unknown case), wrong-actor 403 (self-approve, unrelated user, role mismatch, admin/persona gating), state-machine 409 (step-skip, double-decision, terminal Rejected/Completed, cross-flow ordering), reject flow, and the full delegation authz cycle (denied → delegate → allowed → revoke → denied).
- **D. Reset feature** — factory-wipe → reseed → register/publish, then confirms a flow still runs end-to-end after a reset, and leaves a clean slate.

## Manual browser checks (UI-only)

Boot `bpm-ui` (5173) and `bpm-admin-ui` (5174); these can't be asserted via API:

1. **Launcher** — bpm-ui Home Quick Actions lists all 10 Published flows.
2. **Form validation** — LEAVE form: empty submit blocked; end<start shows "起訖反向" inline; sick leave (病假) flips the cert field to required and blocks without an attachment. No request reaches the server (case count stays 0).
3. **Graceful errors** — `/apply/<unknown>` shows the "還沒煮好" page; `/cases/LEAVE/<random-guid>` shows "載入失敗：HTTP 404" with a back button; no white screen / uncaught JS exceptions in the console.
4. **Reset tab** — admin-ui → Site Setting → Reset: the type-"RESET"-to-confirm dialog gates the red button; the 3 steps show live counts; **after success the admin is logged out to the login page** (identity was rebuilt, so the session is intentionally invalidated) — re-login with the prefilled demo creds and confirm User & Role shows the fresh 13 users.

## Notes / gotchas

- The Reset reseeds identity with **new GUIDs** and clears `Admin_UserSessions`, so the signed-in admin's session necessarily dies — the ResetTab logs out gracefully rather than leaving a zombie session. If you change reset behaviour, re-check this.
- Role resolution honours **dept/group inheritance** (e.g. HR_MANAGER is granted to the HR dept, not a user directly). If a role step 409s with "no user assigned to role:X", check the seed grant + that the resolver expands membership — don't assume a direct user grant.
- GUIDs are stored UPPERCASE as TEXT by EF; sqlite3 `WHERE Id='lowercase'` matches nothing.

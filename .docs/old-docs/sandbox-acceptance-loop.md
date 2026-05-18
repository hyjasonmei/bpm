# Sandbox Acceptance Loop

End-to-end demo path for "no-pain UAT" (selling point 2). One tester runs the
entire flow without touching real recipients, real time, or real state.

## Prereqs

- `bpm-svc` running with `BPM_AUTH_MODE=dev` (default) and `BPM_SEED_ON_STARTUP=true`.
- `bpm-admin-ui` (port 5174) and `bpm-ui` (port 5173) up.
- A bundle to exercise — either an existing flow library entry or one
  produced via the onboarding wizard.

## Steps

### 1. Toggle sandbox ON

`Admin → Site Settings → Sandbox` — flip the toggle. Banner appears across
both UIs: `SANDBOX MODE ACTIVE — captured: 0 mail / 0 webhook · clock +0`.

> Defense-in-depth: set `BPM_SANDBOX_TOGGLE_DISABLED=true` in prod to make
> the PUT endpoint return 403. Toggle then has to be flipped via DB / config.

### 2. Install the bundle (if needed)

`Admin → Flow Library → Import` — drag-drop the bundle zip; pick `Install`
mode so the spec is registered for the tenant.

### 3. Submit an instance as the initiator

End-user app → `Create → Leave Request` (or whichever form the bundle
ships). Fill in dates, hit `Submit`. Confirmation appears on Home.

### 4. Advance time to trigger SLA

`Admin → Sandbox Mailbox → Clock` — click `+1d` (or set days/hours/minutes
and `Advance`). Banner offset updates immediately. SLA-aware notifications
fire against the new "now."

### 5. Inspect captured messages

`Admin → Sandbox Mailbox → Mail` — every email destined for a real recipient
shows up here (rendered HTML body, intended recipients, originating
notification id). The unread badge on the nav matches the captured count.
`Webhooks` tab shows redirected POSTs with their pretty-printed payloads.

### 6. Switch persona to the approver

End-user app → click the persona dropdown. The `Sandbox personas` section
lists every active seed user. Pick the manager — the page reloads under a
new JWT minted by `POST /api/sandbox/persona`. The "Acting as <name>
(sandbox)" pill confirms it. Audit interceptor stamps your real admin id
onto every row the persona writes.

### 7. Approve

The approver sees the open task on Home. Approve it. New captures appear
in the mailbox (approved-notification email + customer-system webhook).

### 8. Reset state

`Admin → Sandbox Mailbox → Clock → Reset` clears the offset.
`POST /api/sandbox/reset/all` (or per-instance) wipes ProcessInstances /
Tasks / TaskHistory / CapturedMessages for the tenant. Specs, org, and
the sandbox-on toggle are preserved so the next loop starts clean.

### 9. Re-run

Submit the same scenario again — should produce identical captures
(modulo timestamps). Bundle-side automation lands in PR-J6 with the dual-DB
E2E test that asserts this invariant.

## Audit hardening

- Sandbox toggle: Info log on every flip with previous→next + actor.
- Persona issuance: Info log on every `IssueSandboxPersonaToken` with
  actor + persona + expiry.
- Reset (instance / all): Info log with deleted-row counts.
- Dedicated audit tables (`SandboxClockEvent`, `ResetEvent`) deferred —
  Info-level grep trail covers v1's needs.

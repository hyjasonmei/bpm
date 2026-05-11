## Why

「無痛上線驗收」 is selling point #2 (alongside AI onboarding). The product promise: **one acceptance tester can complete the entire UAT pass alone** — no coordinating five real users to log in in sequence, no risk of UAT email leaking to real customers, no chance of UAT webhooks corrupting the customer's downstream ERP / HR systems.

Most BPM platforms make UAT painful precisely because they don't solve the lonely-tester problem: every approval needs a different real user, every notification fires for real, every webhook hits the real downstream. Camunda / Bizagi / Pega ship the runtime and leave UAT to the customer's devops team. Our pitch differentiates on this.

The foundation is partly built but nowhere is it described as a coherent capability. Today:

- `bpm-svc/src/Application/Sandbox/IOutboundGate.cs` intercepts email / webhook / SMS and returns `PassThrough` / `Rewrote` / `DropMessage`
- `bpm-svc/src/Domain/Entities/Sandbox/SandboxRedirect.cs` logs each interception (but only `SampleSubject` — body is dropped on the floor)
- `bpm-svc/src/Application/Sandbox/ISandboxService.cs` exposes status toggle + redirect log query
- `bpm-svc/src/Api/Sandbox/SandboxController.cs` wires `/api/sandbox/status` + `/api/sandbox/redirects`
- `bpm-admin-ui/src/components/SandboxBanner.tsx` and `bpm-ui/src/components/SandboxBanner.tsx` show "you are in sandbox" warning
- `Impersonation.tsx` exists in admin UI with full impersonation infrastructure (different audience: prod debugging, not UAT)

What's missing is everything that turns this into a **solo-tester UAT loop**:

1. **Captured Mail** — the redirected email's full body has to be readable, filtered by intended recipient, so the tester can verify "Mary's approval email actually contains the correct line items"
2. **Webhook Capture** — the redirected webhook payload has to be inspectable, and the gate needs to *fake a 200 OK* so the workflow continues, instead of redirecting to a real second URL
3. **Time advance** — testing SLA breach should not require waiting 48 hours; the tester needs to push the system clock forward
4. **State reset** — re-running the same scenario must be one click, not a database surgery
5. **Server-side persona** — the tester needs to *become* Mary on the server (not just swap a frontend JWT) so authorization, audit, and history fields all read like Mary really submitted
6. **Bundle test-case integration** — a bundle's `test-cases/*.json` should auto-drive the sandbox: assert each captured email matches the expected template, each webhook payload matches the expected event, the final ProcessInstance reaches the expected node — fully automated UAT that the tester just clicks "Run"

This change wraps all of the above into one capability set so we can ship a coherent demo: open the Sandbox screen → pick a persona → submit the LEAVE form → click time-advance to trigger SLA → see the captured warning email → switch persona to manager → approve → see the captured "approved" email → click reset → reproduce identically. **One person, five minutes, full UAT.**

## What Changes

### Outbound gate evolves from "drop or rewrite" to "always capture"

Today `IOutboundGate.ApplyAsync` returns `Dropped` or `Rewrote`. Going forward in sandbox mode, the gate ALWAYS captures the full payload to a new `SandboxCapturedMessage` table before deciding the gate outcome, and the default outcome becomes `Captured` (workflow continues as if delivery succeeded; nothing is sent to the outside world). The legacy `Rewrote`-to-fallback-address mode stays as an opt-in for dev hand-testing of real mail clients but is no longer the UAT default.

### `SandboxCapturedMessage` replaces `SandboxRedirect` as the record of truth

`SandboxRedirect` (subject-only, throw-away) is superseded by `SandboxCapturedMessage`:

- `Id`, `TenantId`, `ProcessInstanceId?`, `TaskId?` — workflow context links
- `Channel` (Email / Webhook / Sms)
- `IntendedRecipients` — who SHOULD have received in production
- `Subject`, `BodyHtml`, `BodyText` (Email)
- `Url`, `Headers`, `PayloadJson`, `EventType` (Webhook)
- `Body` (Sms)
- `CapturedAt`, `ReadByUserIds` (the sandbox mailbox tracks read state per persona)
- `OriginatingNotificationId?`, `OriginatingWebhookSubscriptionId?` — link back to the spec rule that fired

Old `SandboxRedirect` rows are migrated as best-effort historical records (subject only) but writes go to the new table.

### Sandbox Mailbox UI (new capability `bpm-sandbox-message-capture`)

New admin screen `Sandbox Mailbox` with three tabs:

- **Mail** — captured emails listed by `CapturedAt DESC`, filterable by intended recipient (drop-down of personas seen) and by ProcessInstance. Click → modal showing rendered HTML body + headers + intended recipients + originating notification id (deep-link to spec rule)
- **Webhooks** — captured webhook deliveries listed similarly. Click → modal showing URL / headers / pretty-printed payload JSON / originating subscription id. A "fake response" badge confirms the gate returned 200 OK without actually calling the URL
- **SMS** — same pattern (low priority for v1; Email + Webhook are the two real customer asks)

A small Bell-style indicator in `AdminLayout` and `AppLayout` shows the unread captured count when sandbox is on; clicking it opens the Mailbox.

### Sandbox-aware clock (new capability `bpm-sandbox-clock-and-state`)

`IClock` is already injected (see `bpm-svc/src/Api/Program.cs:205` `(HttpContext ctx, IClock clock)`). Today its implementation returns `DateTimeOffset.UtcNow`. New behavior:

- A `SandboxClock` decorator wraps the real clock. When sandbox is OFF, it pass-throughs. When sandbox is ON, it adds `currentSandboxOffset` (a TimeSpan) to every read.
- `POST /api/sandbox/clock/advance` `{ delta: "P1D" }` adds to the offset (ISO 8601 duration string parsed; or simpler `{ days, hours, minutes }`).
- `POST /api/sandbox/clock/reset` clears the offset.
- `GET /api/sandbox/clock` returns `{ realNow, sandboxNow, offsetSeconds }`.
- After advance, the sandbox runner triggers any `SlaTimerJob` / `WebhookDispatchWorker` / scheduled-job pass that would otherwise wake on a timer, so the tester sees consequences immediately without waiting up to 60 seconds for the next worker tick.

### Sandbox state reset (new capability `bpm-sandbox-clock-and-state`)

Two reset levels:

- **Per-instance**: `POST /api/sandbox/reset/instance/{id}` — deletes the ProcessInstance + Tasks + TaskHistory + CapturedMessages for one case. Useful when re-running a single scenario.
- **Full**: `POST /api/sandbox/reset/all` — wipes ALL ProcessInstances, Tasks, TaskHistory, CapturedMessages, and resets the clock. Org / Spec / Bundle data is untouched. Requires admin role.

Reset is hard-deleted (not soft) because sandbox data is by definition disposable; soft-delete here would just clutter the mailbox forever.

### Server-side sandbox persona switch (new capability `bpm-acceptance-sandbox`)

Today's `RoleSwitcher` in `bpm-ui` swaps the persona client-side via dev-login JWT mint. That works for runtime demo but the audit / TaskHistory rows still reflect "admin acting as persona". For UAT we need the *server* to treat the requesting tester as the persona — every history row, every captured email's intended recipient, every notification recipient resolution should look exactly like the real persona did the action.

- New endpoint: `POST /api/sandbox/persona` `{ userId: <Guid> }` — issues a new JWT whose `sub` claim is the persona's user id, but whose claims include `sandbox_actor=true` and the original tester's id under `actual_actor_id`. All controllers continue to read `sub` (no change). The TaskHistory event-emitter writes `payload.sandboxActualActor = actual_actor_id` so post-UAT we can prove "this was actually Jason testing as Mary, not Mary herself".
- Frontend: an enhanced `RoleSwitcher` in sandbox mode lists the bundle's `sample-org.json` users (not just hard-coded admin / manager / employee), calls `POST /api/sandbox/persona`, swaps the JWT, refetches state.
- Refuses to issue a sandbox-persona token when sandbox is OFF.

### Bundle test-case integration (couples with `add-spec-bundle-and-flow-library`)

A bundle's `test-cases/*.json` now drives the sandbox automatically:

- `BundleReproducibilityRunner` (specced in `add-spec-bundle-and-flow-library`) runs in sandbox-on mode, executes each test-case, and asserts:
  - Final node trace matches `expectedTrace`
  - Captured emails match `expectedNotifications` (subject substring match + recipient resolution)
  - Captured webhooks match `expectedWebhooks` (event type + payload structural diff)
- Test-case format gets `expectedNotifications[]` and `expectedWebhooks[]` fields added; backwards-compatible (omitted = no assertion on that channel).

This means the bundle-import "Install for runtime" gate becomes a much stronger acceptance check — not just "the routing reached the right end node" but "Mary actually got the right email at the right step, and the ERP webhook fired with the right payload." That is exactly what an acceptance tester verifies manually today.

### `SandboxBanner` upgrade

Both UIs' SandboxBanner gets a "captured: 12 mail / 4 webhook" live counter that links into the Mailbox.

## Capabilities

### New Capabilities
- `bpm-acceptance-sandbox`: umbrella concept — sandbox mode lifecycle, server-side persona switch, sandbox banner, integration with bundle reproducibility
- `bpm-sandbox-message-capture`: outbound gate captures full email / webhook / SMS payloads, mailbox UI, per-persona inbox filter, fake-200 webhook handling
- `bpm-sandbox-clock-and-state`: sandbox-aware IClock decorator, time advance API, scheduled-job kick-off after advance, per-instance + full state reset

### Modified Capabilities
- `bpm-spec-bundle` (specced in `add-spec-bundle-and-flow-library`): test-case format gains `expectedNotifications[]` + `expectedWebhooks[]`; reproducibility runner couples with sandbox capture
- (existing in-code-no-spec) `IOutboundGate`: default sandbox outcome changes from `Dropped` / `Rewrote` to `Captured`; legacy `Rewrote`-to-fallback stays as opt-in
- (existing in-code-no-spec) `ISandboxService`: gains mailbox query / clock advance / state reset methods
- `bpm-admin-shell` (specced informally in `bpm-admin-ui`): adds Sandbox Mailbox nav entry, captured-count indicator

## Impact

- **Existing entity replaced**: `SandboxRedirect` deprecated; `SandboxCapturedMessage` replaces it. Migration: keep old rows for read-back, all new writes go to new table; one release later drop `SandboxRedirect`.
- **New EF entity**: `SandboxCapturedMessage` (~10 columns + JSON blob fields). New migration.
- **New API endpoints** under `/api/sandbox`:
  - `GET /captured` (list with filters), `GET /captured/{id}` (detail), `POST /captured/{id}/read`
  - `POST /clock/advance`, `POST /clock/reset`, `GET /clock`
  - `POST /reset/instance/{id}`, `POST /reset/all`
  - `POST /persona`
- **New `IClock` decorator**: `SandboxClock` registered ahead of `SystemClock` in DI; reads sandbox state per request to decide pass-through vs offset.
- **Frontend changes**:
  - New screen `bpm-admin-ui/src/screens/sandbox/SandboxMailbox.tsx` (Mail / Webhook / Sms tabs, list + detail modals)
  - `RoleSwitcher` in `bpm-ui` extended to fetch `sample-org` users when sandbox is on, call `/api/sandbox/persona`
  - `SandboxBanner` in both UIs: captured counter + clock offset display
  - `AdminLayout` + `AppLayout`: add Bell-like captured indicator (only when sandbox on)
- **Tests**:
  - Unit: `SandboxClock` returns offset-adjusted time; persona endpoint refuses when sandbox off
  - Integration: full UAT loop — submit LEAVE → advance time 48h → assert SLA breach captured email → reset → re-run identically
  - E2E: bundle with one test-case that exercises notification + webhook → install via `mode=install` → reproducibility runner asserts captured payloads
- **Out of scope**:
  - Real SMTP / SMS gateway integration (still goes through `IOutboundGate` in production, just a different gate impl)
  - Sandbox per-tenant isolation in a multi-tenant deployment (POC is single-tenant; multi-tenant sandbox is a Phase B concern)
  - Replay of captured webhooks against the real downstream URL (one-click "send for real now" — explicitly deferred; sandbox is observational only)
  - Persona switch in production (impersonation already covers that need with audit; sandbox persona is *only* valid in sandbox mode)
- **Demo path** (the actual sales pitch this is built around):
  1. Admin toggles sandbox ON via SandboxBanner
  2. Admin opens Flow Library, installs `LEAVE_v1.zip` — repro runner shows green
  3. Admin opens Sandbox Mailbox, drains it
  4. Admin uses RoleSwitcher to become Wilson, submits a LEAVE form
  5. Admin checks Sandbox Mailbox — captured email "您的請假已送出，等待主管核准"
  6. Admin advances clock 48h — captured email "請假申請逾期 SLA"
  7. Admin switches to Mary, approves
  8. Admin checks Mailbox — captured email "您的請假已核准" + captured webhook to the customer's HR system
  9. Admin clicks Reset → all clean
  → One person, five minutes, full UAT.

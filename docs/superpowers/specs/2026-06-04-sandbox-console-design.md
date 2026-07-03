# Sandbox Console (admin-ui) — Design

**Date:** 2026-06-04
**Status:** Approved (開發者, Telegram) — Option A + state reset
**Owner:** lead (admin-svc / admin-ui / bpm-svc shared shell)

## Goal

Replace the `/audit`-adjacent `Sandbox` `PagePlaceholder` in `bpm-admin-ui`
with a working UAT/acceptance console. Two scopes — **global** and
**per-flow** — over three tools plus reset:

- 攔信 (mail/notification capture)
- persona switch (act-as a test user)
- 時間快轉 (clock advance / reset)
- state reset (wipe test cases back to a clean slate)

This is the second of flowcook's two headline selling points (無痛上線驗收).

## Context: what already exists

The global sandbox backend was built for the **retired Model-A runtime**
and is partially orphaned from the current **Model-B chef flows**:

| Tool | Works for Model-B today? | Why |
|---|---|---|
| Clock warp | ✅ | `SandboxClock` decorates the global `IClock`; every Model-B state machine reads `IClock.UtcNow`. |
| Persona switch | ✅ | Session JWT (`/api/sandbox/persona`), flow-agnostic. |
| Sandbox toggle | ✅ | Global `TenantSettings.SandboxMode`. |
| Mail capture | ❌ | Model-B notifies via `INotifyDispatcher` → in-app bell + file log. Nothing hits the `IOutboundGate`; the mailbox is fed only by the Model-A `SandboxCapturingNotificationDispatcher` (`INotificationDispatcher`, `SpecSnapshot`-driven). |
| State reset | ❌ | `ResetService` hard-deletes `ProcessInstance`/`ProcessTask`/`TaskHistory` (Model-A). Model-B cases live in `<CODE>_V<N>_Case` tables and are untouched. |

Existing API surface (all under `bpm-svc` `/api/sandbox`, role/sandbox gated):
status (toggle), clock get/advance/reset, persona/personas,
captured list/detail/read/unread-count, reset/instance + reset/all.

Existing bridge: `bpm-admin-ui/vite.config.ts` proxies `/bpmsvc` → bpm-svc
(`http://localhost:5290`). branding/reports/flow-codes already call bpm-svc
directly this way with `[AllowAnonymous]` (POC; admin↔bpm token bridge deferred).

The Model-B notify message already carries `flowCode` + `caseId` in its
`Context` dict (added during the bell work) — so **per-flow capture is natural**.

## Design

### 1. Mail capture wiring (Model-B) — core new work

- New `SandboxCaptureNotifyDispatcher : INotifyDispatcher` in
  `bpm-svc/src/Persistence/Notifications/`. Registered into the existing
  `CompositeNotifyDispatcher` chain alongside `InAppNotifyDispatcher` and
  `FileNotifyDispatcher` (Persistence DI). A throwing sink must not stop the
  others (composite already swallows-and-rethrows-first).
- On `DispatchAsync(NotifyMessage)`: read `flowCode`/`caseId` from `Context`;
  compute **effective capture** = global `SandboxMode` **OR**
  `FlowSandboxConfig[flowCode].CaptureEnabled`. If on, write one
  `SandboxCapturedMessage` (Subject, BodyText=Body, IntendedRecipients from
  `message.Recipients`, FlowCode, CaseId, CapturedAt=`IClock.UtcNow`,
  OriginatingNotificationId=`SourceId`). The in-app bell still fires — capture
  is additive, never blocks delivery.
- No real email is sent (there is no email sink); the sandbox mailbox **is**
  the "what would have gone out" record.

### 2. Per-flow config

- New entity `FlowSandboxConfig` (Domain `Entities/Sandbox/`):
  `Id, TenantCode, FlowCode, CaptureEnabled, UpdatedAt`. Config in Persistence,
  PK/unique on `(TenantCode, FlowCode)`. Migration `AddFlowSandboxConfig`.
- Effective rule (single source, used by the capture dispatcher):
  `flow captured ⇔ SandboxMode OR FlowSandboxConfig[flow].CaptureEnabled`.

### 3. Schema change to SandboxCapturedMessage

- Add nullable `FlowCode` (maxlen 64) + `CaseId` (Guid?) columns + an index on
  `(TenantCode, FlowCode, CapturedAt)`. Same migration as §2 or its own.
- Mailbox list endpoint gains a `flowCode` filter param.

### 4. State reset — align to Model-B

- Extend `ResetService` to also clear Model-B cases. Discover case tables by
  reflection over the EF model: `db.Model.GetEntityTypes()` filtered by
  `^(?<code>.+)_V\d+_Case$` (same regex used by ReportsController /
  FlowCodesController — new flows auto-covered, nothing hardcoded). For each,
  `ExecuteDeleteAsync` scoped to the tenant. Keep deleting the captured
  messages + zeroing the clock offset (clean slate). Per-flow reset = clear one
  flow's `<CODE>_V*_Case` rows + that flow's captured messages.
- Endpoints: keep `reset/all`; add `reset/flow/{flowCode}` (per-flow).
  Both stay sandbox-gated (refuse when SandboxMode off, to avoid nuking prod).

### 5. New / adjusted API (bpm-svc `/api/sandbox`)

| Route | Verb | Purpose |
|---|---|---|
| `/api/sandbox/flows` | GET | List deployed flows + each `captureEnabled` |
| `/api/sandbox/flows/{flowCode}` | PUT | Set per-flow capture on/off |
| `/api/sandbox/captured?flowCode=` | GET | (extend) filter mailbox by flow |
| `/api/sandbox/reset/flow/{flowCode}` | POST | Per-flow clean slate |

POC auth: the console-facing endpoints are `[AllowAnonymous]` (consistent with
branding/reports). Real admin↔bpm auth bridge is deferred.

### 6. admin-ui Sandbox page

`bpm-admin-ui/src/flowcook/pages/SandboxPage.tsx` + `api/sandbox.ts`, wired into
`AppShell` replacing the placeholder. Layout (sections, not tabs — keep it one
scannable console):

1. **Header strip** — master sandbox toggle (on/off) + sandbox-time vs real-time
   readout (live offset).
2. **時間快轉** — quick buttons (+1d / +1h / +15m / custom) + reset-to-now.
3. **Persona** — list test users; "以此身分開啟 BPM" mints a sandbox-persona
   token and opens bpm-ui carrying it (see §7).
4. **攔信信箱** — captured-notification ledger; filter by flow + channel;
   row-expand shows subject / body / intended recipients; unread badge.
5. **逐流程** — table of deployed flows, each with a capture toggle + a
   "清此流程" (per-flow reset) action. A global "全部清空" lives here too.

### 7. Persona carry into bpm-ui (minimal)

`/api/sandbox/persona` already mints a persona JWT. To make it usable, bpm-ui
accepts the token via a URL param on load (`?sandboxToken=…`): if present, store
it as the session token and strip the param (history.replaceState), then proceed
as that persona. The admin console's "以此身分開啟 BPM" opens
`http://localhost:5173/?sandboxToken=<token>` in a new tab. Small bpm-ui shell
addition; without it the minted token is dead weight.

## Out of scope

- Webhook-redirect capture (開發者's list was 攔信 / persona / 時間快轉 + reset).
- Multi-tenant scoping (per-customer deploy; "tenant" ≡ "global" here).
- Real SMTP/email sink and real admin↔bpm auth bridge (POC deferrals).

## Files (touch list)

**bpm-svc**
- `src/Persistence/Notifications/SandboxCaptureNotifyDispatcher.cs` (new)
- `src/Persistence/DependencyInjection.cs` (register into composite)
- `src/Domain/Entities/Sandbox/FlowSandboxConfig.cs` (new)
- `src/Persistence/Configurations/Sandbox/FlowSandboxConfigConfiguration.cs` (new)
- `src/Domain/Entities/Sandbox/SandboxCapturedMessage.cs` (+FlowCode/CaseId)
- `src/Persistence/Configurations/Sandbox/SandboxCapturedMessageConfiguration.cs` (index)
- `src/Application/Sandbox/IResetService.cs` + `ResetService` (Model-B scan + per-flow)
- `src/Application/Sandbox/IMailboxService` + impl (flowCode filter)
- `src/Api/Sandbox/SandboxController.cs` (flows GET/PUT, reset/flow, captured filter, AllowAnonymous on console endpoints)
- `src/Persistence/Migrations/*` (AddFlowSandboxConfig + capture cols)

**bpm-ui**
- shell entry (App/main) — `?sandboxToken=` pickup

**bpm-admin-ui**
- `src/flowcook/api/sandbox.ts` (new)
- `src/flowcook/pages/SandboxPage.tsx` (new)
- `src/flowcook/app/AppShell.tsx` (wire route)

## Verification

- bpm-svc build + migrate clean; admin-ui + bpm-ui `tsc -p tsconfig.app.json`.
- Chrome: toggle sandbox; advance clock and confirm readout; switch persona →
  bpm-ui opens as that user; submit a flow with sandbox on → notification shows
  in the mailbox (not real-sent), filterable by flow; per-flow toggle works with
  global off; per-flow + global reset clears the right `<CODE>_V*_Case` rows.
- New throwaway `DEMOFLOW_V2_Case` proves reflection auto-discovery (then remove),
  mirroring the report/flow-codes proof.

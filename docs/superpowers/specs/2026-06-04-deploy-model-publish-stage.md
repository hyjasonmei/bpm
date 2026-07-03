# Deploy model: split approve/publish + remove multi-environment

**Date:** 2026-06-04
**Status:** Approved (開發者, Telegram) — "拆兩段 go" + remove Environment.
**Owner:** lead (admin-svc + bpm-svc + both UIs)

## Why

Deploy model is per-customer/per-environment: **one bpm+admin stack = one
environment = one DB**. The in-app `Environment` / `FlowDeployment` tables track
a DEV/STG/PRD matrix from a single admin — redundant once each env is its own DB,
and (per their own code comments) pure bookkeeping the runtime never reads. The
real per-env live state already lives in each env's own `Admin_Flows`.

Separately, the flow lifecycle conflates "reviewed" and "live": the launcher
gates on `State == Approved`, so approving = publishing in one step. 開發者 wants
the two split (approve = reviewed; publish = live in this env).

## Two changes (one cohesive PR)

### 1. Split approve / publish

Add `FlowState.Published = 8` (and the `SharedFlowState` mirror). Lifecycle:

```
Committed ──approve──► Approved ──publish──► Published ──(retire)──► Retired
                          ▲                      │
                          └──────unpublish───────┘
```

- `ApproveAsync` (Committed → Approved) — unchanged; "reviewed/ready".
- `PublishAsync` (Approved → Published) — new; "live in this env".
- `UnpublishAsync` (Published → Approved) — new; take offline, stay reviewed.
- `RetireAsync` — extend to allow from **Published or Approved** → Retired.
- `ReviveAsync` (Retired → Approved) — unchanged.

**The launcher gate flips from `Approved` to `Published`.** A flow appears in
bpm-ui only when its code/manifest ships AND it is `Published` in this env's DB.

`register-shipped` (the blank-env one-click) inserts directly as **Published** —
its whole job is "make every deployed flow live in this clean env". Idempotent.

No DB column change (State is already an int); the migration only drops the two
removed tables.

### 2. Remove Environment / FlowDeployment

Delete the multi-env matrix end to end. Each env's own `Admin_Flows.Published`
already encodes "live here", so nothing is lost.

## Touch list

**admin-svc (remove)**
- Domain: `Flows/Environment.cs`, `Flows/FlowDeployment.cs` (+ `FlowDeploymentStatus`)
- Application: `Flows/IEnvironmentService.cs`, `Flows/IFlowDeploymentService.cs` (+ DTOs)
- Persistence: `Flows/EnvironmentService.cs`, `Flows/FlowDeploymentService.cs`,
  `Configurations/EnvironmentConfiguration.cs`, `Configurations/FlowDeploymentConfiguration.cs`,
  `AdminDbContext` DbSets
- Api: `Controllers/EnvironmentsController.cs`; `FlowsController` — drop
  `GET {id}/deployments` + `POST {id}/deployments/{envId}` (+ `SetDeploymentBody`,
  `FlowDeploymentDto`)
- `Api/Program.cs` DI registrations; `Api/Common/AiBackend.cs` ref; `SeedCli/Program.cs`
  environment seeding
- Migration `DropEnvironmentsAndDeployments` (drop `Admin_Environments`,
  `Admin_FlowDeployments`)

**admin-svc (add)**
- `FlowState.Published`; `IFlowLifecycleService.PublishAsync/UnpublishAsync` +
  impl + `RetireAsync` from Published; `FlowsController` `POST {id}/publish` +
  `POST {id}/unpublish`; `RegisterShippedAsync` → Published.

**bpm-svc**
- `SharedFlowState.Published` (mirror enum value). The flow-registry endpoint
  returns State as-is (already does) — no filter change there.

**bpm-ui**
- `types/process.ts` (or the flow-registry types): add `'Published'` to the state
  union. Flip the launcher gate `state === 'Approved'` → `state === 'Published'`
  in `screens/CreateIndex.tsx`, `screens/Home.tsx`, and
  `components/ui/flow-state-banner/FlowStateBanner.tsx` (+ `hooks/useFlowRegistry`
  if it maps state).

**admin-ui (remove)**
- `pages/sitesetting/EnvironmentsTab.tsx`, `api/environments.ts`; drop the
  Environments tab from `SiteSettingPage`; remove the per-env deploy UI in
  `pages/aiKitchen/ServePanel.tsx` (+ `aiKitchen/types.ts` deployment types,
  `CookPanel` refs).

**admin-ui (add)**
- Serve tab: **Publish** / **Unpublish** buttons next to Approve (Approve =
  Committed→Approved; Publish = Approved→Published; Unpublish = Published→Approved).
  `api/flows` publish/unpublish calls.

## Out of scope

- Cross-environment overview (live-in-DEV-vs-PROD) — belongs in a CD pipeline /
  a future multi-DB console, not the per-stack admin.
- Auto-publish on deploy — publish stays a deliberate action (register-shipped is
  the bulk shortcut for blank-env bootstrap).

## Verification

- Lifecycle: cook → Committed → Approve (not yet in launcher) → Publish (now in
  launcher) → Unpublish (gone from launcher, still Approved) → Retire.
- Blank-env: empty Admin_Flows + deployed code → register-shipped → all flows
  Published → all appear in bpm-ui launcher.
- Environment tab gone from Site Setting; no deploy buttons; admin-svc builds
  with the tables dropped; runtime unaffected. All apps build / tsc clean.
- Migration drops both tables on a fresh migrate; existing dev DB re-migrates.

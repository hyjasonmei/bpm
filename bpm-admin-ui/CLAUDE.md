# bpm-admin-ui — admin console notes

Project-wide conventions live in the root `CLAUDE.md`. Backend conventions
live in `../bpm-svc/CLAUDE.md`. This file covers the admin SPA after the
all-flows-real Phase 1 (PR-L1 → PR-L6).

## Wired screens

The three operational consoles are end-to-end wired to the PR-L1..L5
backend. None of them are mocked.

### Process Admin (`screens/processes/`)

Seven sections behind `ProcessAdminShell.tsx`:

- `DefinitionsList.tsx` — installed specs from `/api/admin/flow-library`
- `Designer.tsx` — BPMN canvas + spec editor (active-node highlight + live
  form preview still placeholders; tracked as a follow-up)
- `Simulator.tsx` — calls `IProcessSimulator` (`/api/admin/process-admin/simulate`)
  for dry-run flow execution with trace + notification log
- `LiveCases.tsx` + `LiveCaseDetail.tsx` — `/api/admin/process-admin/cases/active`
  with the four admin intervention endpoints (cancel / reassign / restart /
  step-skip)
- `CompletedCases.tsx` — completed instance browser
- `Reports.tsx` — `IProcessReportingService` (5-minute cache; in-memory
  percentile, will move to DB function when instance count grows)
- `FlowNotifications.tsx` — notification trigger inspector

### Flow Library (`screens/FlowLibrary/`)

`FlowLibrary.tsx` + `BundleDetail.tsx` + `ImportModal.tsx` +
`ReproReportModal.tsx` + `StatusPill.tsx`. Bundle list / detail / import /
export / repro-check, all backed by `/api/admin/flow-library`. Bundle
status flows `Pending → Validated` after a successful repro run.

### Sandbox Mailbox (`screens/sandbox/`)

Four tabs (`MailTab`, `WebhooksTab`, `SmsTab`, `ClockTab`) with count
badges. Backed by the sandbox mailbox API; entries appear when sandbox
mode is on (default in dev) and `OutboundGate` captures instead of sends.

## BundleInstaller pattern (PR-L4)

`bpm-svc/src/SeedCli/Services/BundleInstaller.cs` is the shared install
primitive: build → parse → write `SpecBundle{ Status = Pending }`,
idempotent on `ManifestChecksum`. Both `SeedCli --include-bundles` and
(eventually) `FlowLibraryController.Import`'s "install without repro"
path use it. Repro runs separately — open Flow Library and click
**Repro Check** per bundle, or POST
`/api/admin/flow-library/{id}/repro-check` directly. SeedCli does not
auto-run repro because it requires sandbox + per-spec test cases that are
slow / brittle in batch.

## Onboarding wizard (`screens/onboarding/`)

9-step stepper (admin-ui-split + AI onboarding). The `CoPilotCanvas`,
`ActorRefEditor`, and `ExpressionInput` widgets here are the canonical
spec-authoring UX. Independent of the PR-L1..L6 work; included here for
completeness because it shares persona-switch and admin gate.

## Type-check

`tsc -p tsconfig.app.json --noEmit` (same `-p` rule as bpm-ui — without
the project flag tsc silently skips `src/`). No JS test framework wired;
rely on tsc + manual boot (`npm run dev`, port 5174) + chrome-devtools
screenshots.

## Demo guard

bpm-admin-ui has no demo guard — it was greenfield from `add-bpm-frontend`
+ admin-ui-split. All screens listed above are live code.

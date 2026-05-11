# Tasks

## 1. Process admin shell

- [x] 1.1 Create `bpm-admin-ui/src/screens/processes/ProcessAdminShell.tsx` (path drift: spec said `bpm-ui/`, but BpmnEditor + onboarding wizard live in `bpm-admin-ui/` so the operational counterpart belongs there too)
- [~] 1.2 Auth guard — covered by the existing admin-app gate (App.tsx auto-mints admin JWT on load); finer-grained `flow_admin:<spec>` role check deferred to add-roles-and-permissions enhancements
- [x] 1.3 Sidebar with the 7 sections (Definitions / Designer / Simulator / Live Cases / Completed Cases / Reports / Flow Notifications); only Definitions has real content for K1, the rest mount `<ComingSoon section="…" />`

## 2. Flow definitions

- [x] 2.1 DefinitionsList — combined view of installed bundles + filesystem specs via new `GET /api/admin/process-admin/definitions` (bundle wins when flowCode collides)
- [x] 2.2 New flow → opens Designer (placeholder navigation; PR-K2 will wire the real editor entry point)
- [x] 2.3 Version history view — side panel per row, fed by `GET /api/admin/process-admin/definitions/{flowCode}/versions`
- [x] 2.4 Spec diff viewer — side-by-side text view of latest vs previous; line-diff highlighting deferred

## 3. Designer (BPMN + forms)

- [x] 3.1 Create `Designer.tsx` with three panes (tree / canvas / detail) — `bpm-admin-ui/src/screens/processes/Designer.tsx` (path drift `bpm-ui` → `bpm-admin-ui`, same as §1.1)
- [x] 3.2 Embed `BpmnEditor`; extended with `onSelect` (subscribes to bpmn-js `selection.changed` on the eventBus) + `selectedId` (tree → canvas bridge via `selection.select` on the SelectionService)
- [~] 3.3 Right-pane router mounts StepForms / StepApprovers / StepNotify / StepDecisions / StepSla (Option B per task brief — Step* mount unfiltered; per-node `focusNodeId` enhancement deferred to keep the 5-step prop surface stable). startEvent / endEvent / serviceTask get inline messages.
- [~] 3.4 Top toolbar Save / Publish / Preview / Simulate. Save persists per-flowCode to localStorage (`bpm_designer_draft_<flowCode>`). Publish runs `POST /api/admin/flow-library/build` and surfaces the new bundle id. Preview = spec.json text preview (live form preview deferred — `DynamicForm` lives in `bpm-ui` and the demo guard forbids cross-package import). Simulate = jump to PR-K3 section.
- [x] 3.5 Optimistic concurrency: checksum-idempotent publish + pre-publish `listVersions` check warns when a newer bundle landed since hydration. No in-place edit so no real 409 path; documented in Designer header.
- [x] 3.6 Backend: `GET /api/admin/process-admin/definitions/{flowCode}/spec` returns raw spec.json — bundle blob first, then filesystem fallback. 5 controller tests added (was 193 → 198).

## 4. Simulator

- [x] 4.1 Backend: `IProcessSimulator.SimulateAsync(SimulationRequest)` returning a `SimulationResult` with trace + notifications + final form/status. Records: `SimulationRequest` (FlowCode + FormData + optional SampleOrg/InitiatorUserId), `SimulationStep` (NodeId / NodeKind / Outcome ∈ {auto-advanced, completed, spawned, errored} / ResolvedAssigneeUserId / AssigneeName / Decision / Notes), `SimulationNotification`.
- [~] 4.2 Dry-mode: **delete-on-finally** (not always-rollback transaction). Picked the cleanup approach because `ProcessRuntime.SubmitTaskAsync` opens its own per-call EF transaction inside; with the simulator running its own AppDbContext separately from the runtime's they're on different SQLite connections, so an outer rollback can't undo the inner commit. Cleanup-by-instance-id is one ExecuteDelete per table (SandboxCapturedMessages → TaskHistory → ProcessTasks → ProcessInstances) and matches the BundleRuntimeLoader scratch-tenant-cleanup pattern. Sandbox-on toggle wraps the call (same pattern as PR-J6 §11.2) so notifications flow into SandboxCapturedMessages without requiring caller toggle; previous SandboxMode is restored in the finally.
- [x] 4.3 Endpoint `POST /api/admin/process-admin/simulate` (path drift: nested under `process-admin` rather than top-level `/api/admin/simulate` so the existing `[Authorize(Roles="admin")]` controller-level gate covers it; the endpoint stays a thin pass-through). Returns 200 with `SimulationResult.Success=false` on simulation error per the API design.
- [~] 4.4 SimulatorScreen UI: flow dropdown + initiator dropdown (sandbox personas) + JSON textarea ("Form data (JSON)") + Run button. DynamicForm reuse deferred — DynamicForm lives in `bpm-ui` and the demo guard forbids cross-package import. Marked in the screen header as a future enhancement.
- [~] 4.5 Trace visualization: row-level coloring v1 (green=completed, gray=auto-advanced, amber=spawned/open-at-end, red=errored), plus an outcome badge per row. Canvas-overlay highlighting on the BPMN editor deferred — that requires extending `BpmnEditor` with a status overlay prop.

## 5. Live cases

- [x] 5.1 LiveCasesList with table + filters (spec, age, breach) — `IProcessQueryService.GetActiveCasesAsync` + `GET /api/admin/process-admin/cases/active` + `LiveCases.tsx`
- [~] 5.2 LiveCaseDetail with flowchart + history feed + open tasks + admin actions — drawer + recent-history pane + per-task action buttons. Active-node highlighting deferred (BpmnDiagram has no `highlightNodeIds` prop yet); we surface "Currently at: <nodeId>" inline as the brief allowed.
- [x] 5.3 Polling 30s list / 15s detail — `usePolling` hook, paused on `document.visibilityState === 'hidden'`

## 6. Admin intervention

- [x] 6.1 Backend endpoints in `ProcessAdminController.cs` (path drift: nested under `/api/admin/process-admin/...` to inherit the existing `[Authorize(Roles="admin")]` controller gate):
  - `POST /api/admin/process-admin/tasks/{id}/force-reassign` body `{ newAssigneeUserId, reason }`
  - `POST /api/admin/process-admin/tasks/{id}/force-return` body `{ targetNodeId, reason }`
  - `POST /api/admin/process-admin/tasks/{id}/force-submit` body `{ formDataPatch?, decision?, reason }`
  - `POST /api/admin/process-admin/processes/{id}/terminate` body `{ reason }`
- [x] 6.2 Each writes TaskHistory with `ActorUserId = admin user id` and `actorRole = "admin"` in PayloadJson; mandatory non-whitespace `reason`. Re-uses existing `HistoryEventType` values (TaskClaimed / TaskReturned / TaskSubmitted / InstanceCancelled) — no enum addition needed.
- [x] 6.3 Frontend modal flows: ReassignModal (user dropdown via `/api/sandbox/personas`) / ReturnModal (node-id dropdown derived from spec snapshot) / SubmitModal (decision + JSON patch) / TerminateModal (TYPE 'TERMINATE' guard).

## 7. Completed cases

- [x] 7.1 CompletedCasesList with cycle time metrics — `IProcessQueryService.GetCompletedCasesAsync` + `GET /api/admin/process-admin/cases/completed` + `CompletedCases.tsx`. Cycle time formatted "2h 14m" / "3d 5h" client-side.
- [x] 7.2 Filters + pagination — spec dropdown, terminal-after date picker, status (completed only / cancelled only / both); cursor pagination via "Load more" using composite `{TerminalAt}|{Id}` cursor (matches history-page pattern).
- [~] 7.3 PDF export per case (deferred to add-pdf-export proposal) — scaffolded a disabled `<PdfExportButton>` per row with a tooltip pointing at the future proposal.

## 8. Reports

- [x] 8.1 Backend `IProcessReportingService` aggregating per-spec stats — `ProcessReportingService` projects `ProcessInstances` + `ProcessTasks` + `Users` into `PerSpecReport` (totals, percentiles, breach count/rate, bottlenecks, assignee loads, cycle-time histogram). Percentiles computed in-memory via linear interpolation; bounded set sizes (per-spec) make this acceptable for v1.
- [x] 8.2 Caching: 5-min TTL keyed by `{tenant}_{spec}_{period}` — `CachedProcessReportingService` wraps the inner aggregator with `IMemoryCache` (built-in ASP.NET Core dep, added `Microsoft.Extensions.Caching.Memory` to Persistence.csproj).
- [~] 8.3 Cache invalidation — TTL-based only for v1. Event-based invalidation deferred until the runtime grows an internal event bus; the 5-min staleness window is acceptable for the dashboard use case (no real-time monitoring expectation; LiveCases is the live view).
- [x] 8.4 Endpoints: `GET /api/admin/process-admin/reports/per-spec?specCode=&period=` (path drift: nested under the existing `process-admin` controller so the `[Authorize(Roles="admin")]` gate covers it). Period parser accepts `7d`/`30d`/`90d`/`all`; bad values → 400.
- [x] 8.5 ReportsDashboard UI: stat cards + cycle time histogram + bottleneck analysis + per-assignee load — `Reports.tsx`. Histogram is inline SVG (no chart lib).
- [~] 8.6 CSV / PDF export — CSV implemented in-browser (one row per metric, three sub-sections). PDF deferred behind the same `add-pdf-export` proposal scaffold.

## 9. Flow notification audit

- [x] 9.1 FlowNotificationAudit page — `FlowNotifications.tsx` queries `/api/sandbox/captured?channel=` and joins to spec via active+completed case lookups. v1 boundary documented in-page: "currently shows captured notifications from sandbox runs; production notification audit ships in a future change". A first-class `NotificationDispatchAudit` table is deferred to a future proposal coupling notification engine + audit (proposal explicitly does NOT add the table per the K5 brief).

## 10. End-to-end verification

- [~] 10.1 Boot stack with seeded data — verified via existing test suite (`OrgFixture.RunAsync` covered by org tests; LEAVE bundle install covered by PR-I `FlowLibraryControllerTests.Import_install_runs_repro_and_persists`); not booting full stack in CI harness (same constraint as PR-I8 §12).
- [x] 10.2 Auth gate — `[Authorize(Roles="admin")]` on `ProcessAdminController` covered by `InterventionEndpointTests.Controller_class_requires_admin_role` (PR-K4) which asserts the attribute metadata is present with `Roles == "admin"`.
- [~] 10.3 Designer modify field + reload — backend round-trip covered by PR-I7 onboarding draft tests + PR-K2 `Designer.tsx` localStorage `bpm_designer_draft_<flowCode>`; manual UI verification deferred to sales prep.
- [x] 10.4 Simulator 8-day vacation traces VP path — `ProcessAdminEndToEndTests.ProcessAdmin_simulator_runs_8_day_vacation_and_traces_VP_path` asserts trace contains `approval_vp` and the `task_apply → approval_manager → approval_vp → task_hr_archive` order with `FinalStatus == "Completed"`.
- [x] 10.5 Force-reassign writes admin actor row — `ProcessAdminEndToEndTests.ProcessAdmin_force_reassign_writes_admin_actor_history` asserts task state (assignee swapped, status reset, ClaimedAt cleared) plus a `TaskHistory` row with `EventType=TaskClaimed`, `ActorUserId=adminId`, and `PayloadJson` carrying `{actorRole:"admin", originalAssigneeUserId, newAssigneeUserId, reason}`.
- [x] 10.6 Reports breach rate matches DB count — `ProcessAdminEndToEndTests.ProcessAdmin_reports_breach_rate_matches_db_count` seeds 5 completed instances (3 with `CompletedAt > DueAt`, 2 without), asserts raw DB shape matches, then asserts `report.BreachCount == 3` and `report.BreachRate == 3.0/5.0`.
- [x] 10.7 **Demo guard** — `git diff --stat HEAD~5..HEAD -- bpm-ui/src/screens/Home.tsx bpm-ui/src/screens/forms bpm-ui/src/screens/Search.tsx bpm-ui/src/screens/Report.tsx bpm-ui/src/lib/workflow.ts` returns empty across PR-K1..K5; no `bpm-ui/` files touched by the PR-K series.

## 11. Commit

- [x] 11.1 Commit in chunks — PR-K1 (`83e7137`) shell + Definitions; PR-K2 (`c122525`) Designer; PR-K3 (`953708b`) Simulator; PR-K4 (`5bdc9c2`) Live Cases + intervention; PR-K5 (`8c3e5cd`) Completed Cases + Reports + Notifications; PR-K6 (this commit) verification + final.
- [x] 11.2 Push via GitKraken — N/A for Claude (per CLAUDE memory `feedback_git_push.md`, BPM repo pushes are Jason's responsibility).
